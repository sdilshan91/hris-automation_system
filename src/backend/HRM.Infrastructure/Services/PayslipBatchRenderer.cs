using System.Collections.Concurrent;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Payroll;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// The compute side of payslip generation (US-PAY-004 FR-4/FR-7). Renders + stores every slip's PDF for a
/// run with bounded concurrency (NFR-3), updates each slip's PDF fields, and returns (generated, failed)
/// counts. Tenant-scoped via the EF global query filter (AC-4) — the run + slips + employees + details are
/// all naturally scoped to the run's tenant. Invoked by the Hangfire GeneratePayslipsJob (after it restores
/// the tenant context) or directly in tests.
///
/// <para>FR-8: a failed render/store flips that one slip to <c>Failed</c> and logs the error; the batch
/// continues, and a re-run (AC-5 regenerate) retries the failed slips. NFR-6: storage paths are GUID-derived
/// and asserted safe by <see cref="PayslipStoragePath"/>.</para>
///
/// <para>YTD (BR-4/FR-2): when the tenant enables YTD display, each component's YTD column is the sum of that
/// component (by name) across the run's pay period and prior months of the SAME calendar year. The tenant
/// YTD-enable flag does not yet have a config surface, so it DEFAULTS OFF — see the module note for the
/// deferred per-tenant config.</para>
/// </summary>
public sealed class PayslipBatchRenderer : IPayslipBatchRenderer
{
    private const int MaxConcurrency = 10; // NFR-3: bounded parallelism (PDF render is CPU-bound).
    private const string DefaultDisclaimer =
        "This is a computer-generated document and does not require a signature.";

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<PayslipBatchRenderer> _logger;

    public PayslipBatchRenderer(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IFileStorage fileStorage,
        ILogger<PayslipBatchRenderer> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Result<PayslipBatchResult>> RenderRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipBatchResult>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipBatchResult>.Failure("Payroll run not found.", 404, "run_not_found");

        var slips = await _dbContext.PayrollSlips
            .Where(s => s.PayrollRunId == runId)
            .ToListAsync(cancellationToken);

        if (slips.Count == 0)
            return Result<PayslipBatchResult>.Success(new PayslipBatchResult(0, 0));

        // Bulk-load the supporting data ONCE (avoids N+1 across 5,000 slips, NFR-1).
        var employeeIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        var slipIds = slips.Select(s => s.Id).ToList();

        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var departments = await _dbContext.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        var jobTitles = await _dbContext.JobTitles.AsNoTracking()
            .ToDictionaryAsync(j => j.Id, j => j.TitleName, cancellationToken);

        var detailsBySlip = (await _dbContext.PayrollSlipDetails.AsNoTracking()
                .Where(d => slipIds.Contains(d.PayrollSlipId))
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.PayrollSlipId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var showYtd = TenantYtdEnabled();
        var ytdByComponent = showYtd
            ? await BuildYtdAsync(run.PayYear, run.PayMonth, employeeIds, cancellationToken)
            : null;

        var companyName = string.IsNullOrWhiteSpace(_tenantContext.Subdomain) ? "Company" : _tenantContext.Subdomain;
        var tenantId = _tenantContext.TenantId;

        var generated = 0;
        var failed = 0;
        var updates = new ConcurrentBag<(Guid SlipId, bool Ok, string? Path, int Size)>();

        using var gate = new SemaphoreSlim(MaxConcurrency);
        var tasks = slips.Select(async slip =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var relativePath = PayslipStoragePath.ForSlip(run.Id, slip.EmployeeId); // NFR-6: GUID-derived, asserted safe.
                try
                {
                    employees.TryGetValue(slip.EmployeeId, out var employee);
                    var details = detailsBySlip.GetValueOrDefault(slip.Id, new List<PayrollSlipDetail>());

                    var model = BuildModel(
                        slip, employee, details, companyName, departments, jobTitles, showYtd,
                        ytdByComponent?.GetValueOrDefault((slip.EmployeeId, false)),
                        ytdByComponent?.GetValueOrDefault((slip.EmployeeId, true)));

                    var bytes = PayslipPdfRenderer.Render(model);

                    using var stream = new MemoryStream(bytes, writable: false);
                    await _fileStorage.UploadAsync(tenantId, relativePath, stream, "application/pdf", cancellationToken);

                    updates.Add((slip.Id, true, relativePath, bytes.Length));
                }
                catch (Exception ex)
                {
                    // FR-8: individual failure — flag + log, keep the batch going (retryable on regenerate).
                    _logger.LogError(ex,
                        "Payslip render failed. RunId={RunId}, SlipId={SlipId}, EmployeeId={EmployeeId}, Tenant={TenantId}",
                        run.Id, slip.Id, slip.EmployeeId, tenantId);
                    updates.Add((slip.Id, false, null, 0));
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Apply the updates on the tracked slip entities + persist once.
        var now = DateTime.UtcNow;
        var slipById = slips.ToDictionary(s => s.Id);
        foreach (var (slipId, ok, path, size) in updates)
        {
            var slip = slipById[slipId];
            if (ok)
            {
                slip.PdfStatus = PayslipPdfStatus.Generated;
                slip.PdfStoragePath = path;
                slip.PdfGeneratedAt = now;
                slip.PdfFileSizeBytes = size;
                generated++;
            }
            else
            {
                slip.PdfStatus = PayslipPdfStatus.Failed;
                slip.PdfGeneratedAt = null;
                slip.PdfFileSizeBytes = null;
                failed++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payslip batch render complete. RunId={RunId}, Generated={Generated}, Failed={Failed}, Tenant={TenantId}",
            run.Id, generated, failed, tenantId);

        return Result<PayslipBatchResult>.Success(new PayslipBatchResult(generated, failed));
    }

    /// <summary>
    /// BR-4/FR-2: YTD per (employee, isStatutory-bucket → component name → summed amount) across the run
    /// period and prior months of the SAME calendar year. Earnings and deductions are kept in separate
    /// buckets so a same-named earning/deduction never collide. Tenant-scoped by the global filter.
    /// </summary>
    private async Task<Dictionary<(Guid EmployeeId, bool IsDeductionSide), Dictionary<string, decimal>>> BuildYtdAsync(
        int payYear, int payMonth, IReadOnlyList<Guid> employeeIds, CancellationToken ct)
    {
        // Slips for these employees up to and including the current pay period, same calendar year.
        var priorSlips = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayYear == payYear && s.PayMonth <= payMonth && employeeIds.Contains(s.EmployeeId))
            .Select(s => new { s.Id, s.EmployeeId })
            .ToListAsync(ct);

        var slipToEmployee = priorSlips.ToDictionary(s => s.Id, s => s.EmployeeId);
        var priorSlipIds = priorSlips.Select(s => s.Id).ToList();

        var result = new Dictionary<(Guid, bool), Dictionary<string, decimal>>();
        if (priorSlipIds.Count == 0) return result;

        var details = await _dbContext.PayrollSlipDetails.AsNoTracking()
            .Where(d => priorSlipIds.Contains(d.PayrollSlipId))
            .Select(d => new { d.PayrollSlipId, d.ComponentName, d.ComponentType, d.Amount })
            .ToListAsync(ct);

        foreach (var d in details)
        {
            if (!slipToEmployee.TryGetValue(d.PayrollSlipId, out var empId)) continue;
            var isDeductionSide = IsDeductionSide(d.ComponentType);
            var key = (empId, isDeductionSide);
            if (!result.TryGetValue(key, out var map))
                result[key] = map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            map[d.ComponentName] = map.GetValueOrDefault(d.ComponentName) + d.Amount;
        }

        return result;
    }

    private static PayslipDocumentModel BuildModel(
        PayrollSlip slip,
        Employee? employee,
        List<PayrollSlipDetail> details,
        string companyName,
        IReadOnlyDictionary<Guid, string> departments,
        IReadOnlyDictionary<Guid, string> jobTitles,
        bool showYtd,
        IReadOnlyDictionary<string, decimal>? earningYtd,
        IReadOnlyDictionary<string, decimal>? deductionYtd)
    {
        PayslipLine ToLine(PayrollSlipDetail d, bool deductionSide)
        {
            var isStatutory = string.Equals(d.ComponentType, nameof(SalaryComponentType.Statutory), StringComparison.OrdinalIgnoreCase);
            decimal? ytd = null;
            if (showYtd)
            {
                var map = deductionSide ? deductionYtd : earningYtd;
                if (map is not null && map.TryGetValue(d.ComponentName, out var v)) ytd = v;
            }
            return new PayslipLine(d.ComponentName, isStatutory, d.Amount, ytd);
        }

        var earnings = details.Where(d => !IsDeductionSide(d.ComponentType)).Select(d => ToLine(d, false)).ToList();
        var deductions = details.Where(d => IsDeductionSide(d.ComponentType)).Select(d => ToLine(d, true)).ToList();

        return new PayslipDocumentModel
        {
            CompanyName = companyName,
            CompanyAddress = null, // basic per-tenant branding only (logo/name) — address config deferred (module note).
            CompanyLogoUrl = null,
            BrandPrimaryColor = null,
            FooterDisclaimer = DefaultDisclaimer, // BR-3: tenant-configurable footer deferred — see module note.
            PayMonth = slip.PayMonth,
            PayYear = slip.PayYear,
            EmployeeName = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim(),
            EmployeeNo = employee?.EmployeeNo ?? slip.EmployeeId.ToString(),
            Department = employee is not null ? departments.GetValueOrDefault(employee.DepartmentId) : null,
            Designation = employee is not null ? jobTitles.GetValueOrDefault(employee.JobTitleId) : null,
            DateOfJoining = employee?.DateOfJoining,
            Earnings = earnings,
            Deductions = deductions,
            GrossEarnings = slip.GrossEarnings,
            TotalDeductions = slip.TotalDeductions,
            NetSalary = slip.NetSalary,
            WorkingDays = slip.WorkingDays,
            PaidDays = slip.PaidDays,
            LopDays = slip.LopDays,
            ShowYtd = showYtd,
        };
    }

    /// <summary>Deduction + statutory components reduce net; earning + reimbursement contribute to gross.</summary>
    private static bool IsDeductionSide(string componentType) =>
        string.Equals(componentType, nameof(SalaryComponentType.Deduction), StringComparison.OrdinalIgnoreCase)
        || string.Equals(componentType, nameof(SalaryComponentType.Statutory), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// BR-4: tenant YTD-display flag. No per-tenant config surface exists yet, so this DEFAULTS OFF. The
    /// deferred config (a tenant payroll-settings flag) is noted in the module note; flipping it on is a
    /// one-line change here once the settings entity lands.
    /// </summary>
    private bool TenantYtdEnabled() => false;
}
