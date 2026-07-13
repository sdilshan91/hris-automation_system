using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Payroll;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee self-service payslip read service (US-PAY-005). The crux of this story is authorization, enforced
/// in TWO coordinated layers:
/// <list type="number">
///   <item>Tenant isolation — every query runs through the EF global query filter (tenant_id), so a slip from
///   another tenant is structurally invisible (returns 404 / empty).</item>
///   <item>Employee isolation — the caller's employee_id is resolved from <see cref="ICurrentUser"/> and
///   filtered explicitly. The LIST query filters by it (FR-8). The DETAIL / PDF reads (addressed by a
///   user-supplied <c>payslipId</c>) load the slip then compare its <c>EmployeeId</c> to the caller's: a
///   mismatch is a deliberate 403 (AC-4), NOT a 404 — the story wants Forbidden so URL-manipulation is an
///   explicit authz denial, and never returns the other employee's data (BR-5).</item>
/// </list>
/// Only payslips from Finalized payroll runs are accessible (BR-1) — ReviewPending/Approved/etc. are hidden.
/// </summary>
public sealed class MyPayslipService : IMyPayslipService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;

    public MyPayslipService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<MyPayslipListDto>> ListMineAsync(
        int? year, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MyPayslipListDto>.Failure("Tenant context is not resolved.", 400);

        var employeeId = await ResolveSelfEmployeeIdAsync(cancellationToken);
        if (employeeId is null)
            return Result<MyPayslipListDto>.Failure("No employee record is linked to the current user.", 403, "no_employee_linked");

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? 12 : pageSize;

        // FR-2/BR-1: only slips whose run is Finalized. Join slips -> run, scope to the caller's employee.
        var finalizedRunIds = _dbContext.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Finalized)
            .Select(r => r.Id);

        var query = _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId && finalizedRunIds.Contains(s.PayrollRunId));

        if (year is not null)
            query = query.Where(s => s.PayYear == year);

        var totalCount = await query.CountAsync(cancellationToken);

        var slips = await query
            .OrderByDescending(s => s.PayYear)
            .ThenByDescending(s => s.PayMonth)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new MyPayslipListItemDto
            {
                PayslipId = s.Id,
                PayMonth = s.PayMonth,
                PayYear = s.PayYear,
                GrossEarnings = s.GrossEarnings,
                TotalDeductions = s.TotalDeductions,
                NetSalary = s.NetSalary,
                PaidDays = s.PaidDays,
                LopDays = s.LopDays,
                PdfAvailable = s.PdfStatus == PayslipPdfStatus.Generated && s.PdfStoragePath != null,
            })
            .ToListAsync(cancellationToken);

        return Result<MyPayslipListDto>.Success(new MyPayslipListDto
        {
            Items = slips,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<Result<MyPayslipDetailDto>> GetMineAsync(Guid payslipId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MyPayslipDetailDto>.Failure("Tenant context is not resolved.", 400);

        var employeeId = await ResolveSelfEmployeeIdAsync(cancellationToken);
        if (employeeId is null)
            return Result<MyPayslipDetailDto>.Failure("No employee record is linked to the current user.", 403, "no_employee_linked");

        var authz = await AuthorizeOwnSlipAsync(payslipId, employeeId.Value, cancellationToken);
        if (authz.IsFailure)
            return Result<MyPayslipDetailDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var slip = authz.Value!;

        var details = await _dbContext.PayrollSlipDetails.AsNoTracking()
            .Where(d => d.PayrollSlipId == slip.Id)
            .ToListAsync(cancellationToken);

        // FR-7: YTD column only when the tenant enables it (defaults off — see module note, shared scaffold with US-PAY-004).
        var showYtd = await TenantYtdEnabledAsync(cancellationToken);
        var ytdByComponent = showYtd
            ? await BuildYtdAsync(slip.PayYear, slip.PayMonth, employeeId.Value, cancellationToken)
            : null;

        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId.Value)
            .Select(e => new { e.EmployeeNo, e.FirstName, e.LastName, e.DepartmentId, e.JobTitleId })
            .FirstOrDefaultAsync(cancellationToken);

        // ISSUE-165: prefer the slip's point-in-time snapshot so a historical payslip shows the dept/designation
        // as they were AT GENERATION, not the employee's CURRENT (possibly moved/renamed) ones. Fall back to live
        // resolution ONLY when the snapshot is null (legacy slips generated before the snapshot existed).
        string? department = slip.DepartmentSnapshot;
        string? designation = slip.DesignationSnapshot;
        if (employee is not null)
        {
            if (department is null)
                department = await _dbContext.Departments.AsNoTracking()
                    .Where(d => d.Id == employee.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken);
            if (designation is null)
                designation = await _dbContext.JobTitles.AsNoTracking()
                    .Where(j => j.Id == employee.JobTitleId).Select(j => j.TitleName).FirstOrDefaultAsync(cancellationToken);
        }

        MyPayslipComponentDto ToComponent(PayrollSlipDetail d, bool deductionSide)
        {
            decimal? ytd = null;
            if (showYtd && ytdByComponent is not null)
            {
                var map = ytdByComponent.GetValueOrDefault((employeeId.Value, deductionSide));
                if (map is not null && map.TryGetValue(d.ComponentName, out var v)) ytd = v;
            }
            return new MyPayslipComponentDto { ComponentName = d.ComponentName, Amount = d.Amount, YtdAmount = ytd };
        }

        var earnings = details.Where(d => !IsDeductionSide(d.ComponentType)).Select(d => ToComponent(d, false)).ToList();
        var deductions = details.Where(d => IsDeductionSide(d.ComponentType)).Select(d => ToComponent(d, true)).ToList();

        return Result<MyPayslipDetailDto>.Success(new MyPayslipDetailDto
        {
            PayslipId = slip.Id,
            PayMonth = slip.PayMonth,
            PayYear = slip.PayYear,
            Employee = new MyPayslipEmployeeDto
            {
                Name = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim(),
                EmployeeNo = employee?.EmployeeNo ?? slip.EmployeeId.ToString(),
                Department = department,
                Designation = designation,
            },
            Earnings = earnings,
            Deductions = deductions,
            GrossEarnings = slip.GrossEarnings,
            TotalDeductions = slip.TotalDeductions,
            NetSalary = slip.NetSalary,
            WorkingDays = slip.WorkingDays,
            PaidDays = slip.PaidDays,
            LopDays = slip.LopDays,
        });
    }

    public async Task<Result<PayslipFileDto>> DownloadMineAsync(Guid payslipId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipFileDto>.Failure("Tenant context is not resolved.", 400);

        var employeeId = await ResolveSelfEmployeeIdAsync(cancellationToken);
        if (employeeId is null)
            return Result<PayslipFileDto>.Failure("No employee record is linked to the current user.", 403, "no_employee_linked");

        var authz = await AuthorizeOwnSlipAsync(payslipId, employeeId.Value, cancellationToken);
        if (authz.IsFailure)
            return Result<PayslipFileDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var slip = authz.Value!;

        if (slip.PdfStatus != PayslipPdfStatus.Generated || string.IsNullOrWhiteSpace(slip.PdfStoragePath))
            return Result<PayslipFileDto>.Failure("Payslip PDF has not been generated.", 404, "pdf_not_generated");

        PayslipStoragePath.AssertSafe(slip.PdfStoragePath!);
        await using var stream = await _fileStorage.OpenReadAsync(_tenantContext.TenantId, slip.PdfStoragePath!, cancellationToken);
        if (stream is null)
            return Result<PayslipFileDto>.Failure("Payslip PDF file is missing from storage.", 404, "pdf_missing");

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        var employeeNo = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId.Value).Select(e => e.EmployeeNo).FirstOrDefaultAsync(cancellationToken);

        var fileName = PayslipStoragePath.DownloadFileName(employeeNo ?? employeeId.Value.ToString(), slip.PayMonth, slip.PayYear);
        return Result<PayslipFileDto>.Success(new PayslipFileDto(buffer.ToArray(), fileName, "application/pdf"));
    }

    /// <summary>
    /// Loads a slip by id (tenant-scoped via the global filter) and enforces the self/Finalized authz:
    /// <list type="bullet">
    ///   <item>not found for the tenant → 404 (cross-tenant slips are invisible, so this is the cross-tenant case too).</item>
    ///   <item>belongs to another employee → <b>403</b> (AC-4 — deliberate Forbidden on URL manipulation, NOT 404).</item>
    ///   <item>run is not Finalized → 403 (BR-1 — the slip exists for the caller but is not yet visible to them).</item>
    /// </list>
    /// Returns no other employee's data in any path (BR-5).
    /// </summary>
    private async Task<Result<PayrollSlip>> AuthorizeOwnSlipAsync(Guid payslipId, Guid employeeId, CancellationToken ct)
    {
        var slip = await _dbContext.PayrollSlips.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == payslipId, ct);

        if (slip is null)
            return Result<PayrollSlip>.Failure("Payslip not found.", 404, "payslip_not_found");

        // AC-4 / BR-5: a slip belonging to a different employee is a 403, never disclosed.
        if (slip.EmployeeId != employeeId)
            return Result<PayrollSlip>.Failure("You are not authorized to access this payslip.", 403, "forbidden_payslip");

        // BR-1: only Finalized-run slips are visible to the employee.
        var isFinalized = await _dbContext.PayrollRuns.AsNoTracking()
            .AnyAsync(r => r.Id == slip.PayrollRunId && r.Status == PayrollRunStatus.Finalized, ct);
        if (!isFinalized)
            return Result<PayrollSlip>.Failure("This payslip is not yet available.", 403, "payslip_not_finalized");

        return Result<PayrollSlip>.Success(slip);
    }

    /// <summary>Resolves the caller's employee_id from the current user (FR-5/FR-8). Null when no link exists.</summary>
    private async Task<Guid?> ResolveSelfEmployeeIdAsync(CancellationToken ct)
    {
        var employeeId = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.UserId == _currentUser.UserId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);
        return employeeId;
    }

    /// <summary>Deduction + statutory components reduce net (mirrors PayslipBatchRenderer).</summary>
    private static bool IsDeductionSide(string componentType) =>
        string.Equals(componentType, nameof(SalaryComponentType.Deduction), StringComparison.OrdinalIgnoreCase)
        || string.Equals(componentType, nameof(SalaryComponentType.Statutory), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// FR-7: per-component YTD totals for ONE employee (same-year, up to and including this pay period), bucketed
    /// by earning/deduction side and component name. Mirrors PayslipBatchRenderer.BuildYtdAsync but scoped to the
    /// single self employee. Only invoked when <see cref="TenantYtdEnabled"/> is true (currently off).
    /// </summary>
    private async Task<Dictionary<(Guid EmployeeId, bool IsDeductionSide), Dictionary<string, decimal>>> BuildYtdAsync(
        int payYear, int payMonth, Guid employeeId, CancellationToken ct)
    {
        var priorSlipIds = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayYear == payYear && s.PayMonth <= payMonth && s.EmployeeId == employeeId)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var result = new Dictionary<(Guid, bool), Dictionary<string, decimal>>();
        if (priorSlipIds.Count == 0) return result;

        var details = await _dbContext.PayrollSlipDetails.AsNoTracking()
            .Where(d => priorSlipIds.Contains(d.PayrollSlipId))
            .Select(d => new { d.ComponentName, d.ComponentType, d.Amount })
            .ToListAsync(ct);

        foreach (var d in details)
        {
            var key = (employeeId, IsDeductionSide(d.ComponentType));
            if (!result.TryGetValue(key, out var map))
            {
                map = new Dictionary<string, decimal>();
                result[key] = map;
            }
            map[d.ComponentName] = map.GetValueOrDefault(d.ComponentName) + d.Amount;
        }

        return result;
    }

    /// <summary>
    /// FR-7 / ISSUE-160: per-tenant YTD-display flag, read from <see cref="Tenant.PayslipYtdEnabled"/> (defaults
    /// off — a tenant opts in). The Tenant row is NOT covered by the global query filter (it IS the tenant), so
    /// it is loaded strictly by the resolved tenant id — no cross-tenant access is possible.
    /// </summary>
    private async Task<bool> TenantYtdEnabledAsync(CancellationToken ct)
        => await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.PayslipYtdEnabled)
            .FirstOrDefaultAsync(ct);
}
