using System.Globalization;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PA = HRM.Domain.Payroll.PayrollAuditAction;

namespace HRM.Infrastructure.Services;

/// <summary>
/// CRUD + bulk + document operations for payroll adjustments (US-PAY-007). Tenant-scoped via
/// <see cref="ITenantContext"/> + the EF global query filter (AC-5); supporting documents go to tenant blob
/// storage via the reused <see cref="IFileStorage"/> abstraction (AC-3). The run-engine integration (FR-3
/// pickup + FR-4 mark-Applied) lives in <see cref="PayrollAdjustmentResolver"/>, not here.
///
/// <para>Period-availability rules (BR-7/BR-8): an adjustment cannot target a period whose run is Finalized
/// (BR-7) or has already entered Processing (BR-8) — in either case it is retargeted to the next available
/// period (the first month whose run is not Finalized and not Processing). This is decided here because it
/// needs the run status from the DB.</para>
/// </summary>
public sealed class PayrollAdjustmentService : IPayrollAdjustmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IPayrollAuditLogger _audit;
    private readonly ILogger<PayrollAdjustmentService> _logger;

    // NFR-5: supporting document constraints.
    private const long MaxDocumentBytes = 5L * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/jpg", "image/png",
    };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png",
    };

    // BR-6: cap how many future occurrences a recurring adjustment may generate (a defensive bound).
    private const int MaxRecurringOccurrences = 120; // 10 years of monthly occurrences.

    public PayrollAdjustmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IPayrollAuditLogger audit,
        ILogger<PayrollAdjustmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>US-PAY-012: audited snapshot of an adjustment (before/after JSON).</summary>
    private static object Snapshot(PayrollAdjustment a) => new
    {
        a.EmployeeId, AdjustmentType = a.AdjustmentType.ToString(), a.Amount, a.Description,
        a.ApplicablePayMonth, a.ApplicablePayYear, a.IsTaxable, a.IsRecurring, Status = a.Status.ToString(),
    };

    // ── FR-1: create ────────────────────────────────────────────────────────

    public async Task<Result<CreatePayrollAdjustmentResult>> CreateAsync(
        CreateAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CreatePayrollAdjustmentResult>.Failure("Tenant context is not resolved.", 400);

        if (!Enum.TryParse<AdjustmentType>(input.AdjustmentType, ignoreCase: false, out var type))
            return Result<CreatePayrollAdjustmentResult>.Failure("Invalid adjustment type.", 400, "invalid_adjustment_type");

        // BR-1: the employee must exist and have an active salary assignment.
        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<CreatePayrollAdjustmentResult>.Failure("Employee not found.", 404, "employee_not_found");

        if (!await HasActiveSalaryAsync(input.EmployeeId, cancellationToken))
            return Result<CreatePayrollAdjustmentResult>.Failure(
                "Employee does not have an active salary assignment.", 422, "no_active_salary");

        // BR-5/FR-7: a Correction must reference an existing original slip (tenant-scoped).
        if (type == AdjustmentType.Correction)
        {
            if (input.ReferencePayrollSlipId is not { } refSlip || refSlip == Guid.Empty)
                return Result<CreatePayrollAdjustmentResult>.Failure(
                    "A correction must reference the original payslip.", 422, "correction_reference_required");

            var slipExists = await _dbContext.PayrollSlips.AsNoTracking()
                .AnyAsync(s => s.Id == refSlip, cancellationToken);
            if (!slipExists)
                return Result<CreatePayrollAdjustmentResult>.Failure(
                    "Referenced payslip not found.", 404, "reference_slip_not_found");
        }

        // BR-7/BR-8: retarget to the next available period when the requested one is Finalized/Processing.
        var (targetYear, targetMonth, deferred) =
            await ResolveTargetPeriodAsync(input.ApplicablePayYear, input.ApplicablePayMonth, cancellationToken);

        // BR-3: deduction negative-net warning (non-blocking) — estimated against the period's slip if one exists.
        var negativeNetWarning = type == AdjustmentType.Deduction
            && await WouldDriveNetNegativeAsync(input.EmployeeId, targetYear, targetMonth, input.Amount, cancellationToken);

        var seriesId = input.IsRecurring ? BaseEntity.NewUuidV7() : (Guid?)null;
        var createdBy = _currentUser.UserId.ToString();
        var now = DateTime.UtcNow;

        var first = NewAdjustment(input, type, targetYear, targetMonth, seriesId, createdBy, now);
        _dbContext.PayrollAdjustments.Add(first);

        var generatedOccurrences = 0;
        if (input.IsRecurring && input.RecurrenceEndYear is { } endYear && input.RecurrenceEndMonth is { } endMonth)
        {
            // FR-5/BR-6: auto-create a Pending record for each future period up to the recurrence end.
            var (y, m) = NextPeriod(targetYear, targetMonth);
            var endOrdinal = endYear * 12 + (endMonth - 1);
            while (y * 12 + (m - 1) <= endOrdinal && generatedOccurrences < MaxRecurringOccurrences)
            {
                var occ = NewAdjustment(input, type, y, m, seriesId, createdBy, now);
                _dbContext.PayrollAdjustments.Add(occ);
                generatedOccurrences++;
                (y, m) = NextPeriod(y, m);
            }
        }

        // US-PAY-012 (FR-2): audit the create — before=null, after=snapshot of the primary record. Committed
        // atomically. (Recurring occurrences share the series id captured in the snapshot's period fields.)
        _audit.Log(PA.PayrollAdjustmentCreated, PA.ResourceType.PayrollAdjustment,
            first.Id.ToString(), before: null, after: Snapshot(first));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = ToDto(first, employee, negativeNetWarning);
        var result = new CreatePayrollAdjustmentResult(
            dto, generatedOccurrences, negativeNetWarning,
            deferred ? targetYear : null, deferred ? targetMonth : null);

        return Result<CreatePayrollAdjustmentResult>.Success(result);
    }

    // ── FR-6: cancel ─────────────────────────────────────────────────────────

    public async Task<Result> CancelAsync(Guid adjustmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var adj = await _dbContext.PayrollAdjustments.FirstOrDefaultAsync(a => a.Id == adjustmentId, cancellationToken);
        if (adj is null)
            return Result.Failure("Adjustment not found.", 404, "adjustment_not_found");

        if (adj.Status == AdjustmentStatus.Applied)
            return Result.Failure("An applied adjustment cannot be cancelled.", 409, "adjustment_already_applied");
        if (adj.Status == AdjustmentStatus.Cancelled)
            return Result.Failure("Adjustment is already cancelled.", 409, "adjustment_already_cancelled");

        var before = Snapshot(adj);
        adj.Status = AdjustmentStatus.Cancelled;
        adj.UpdatedAt = DateTime.UtcNow;

        // US-PAY-012 (FR-2): audit the cancellation with before/after JSON.
        _audit.Log(PA.PayrollAdjustmentCancelled, PA.ResourceType.PayrollAdjustment,
            adj.Id.ToString(), before: before, after: Snapshot(adj));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ── List / get ───────────────────────────────────────────────────────────

    public async Task<Result<PayrollAdjustmentPageDto>> ListAsync(
        AdjustmentListFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollAdjustmentPageDto>.Failure("Tenant context is not resolved.", 400);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 200 ? 25 : filter.PageSize;

        var query = _dbContext.PayrollAdjustments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status) && Enum.TryParse<AdjustmentStatus>(filter.Status, out var st))
            query = query.Where(a => a.Status == st);
        if (!string.IsNullOrWhiteSpace(filter.AdjustmentType) && Enum.TryParse<AdjustmentType>(filter.AdjustmentType, out var ty))
            query = query.Where(a => a.AdjustmentType == ty);
        if (filter.PayMonth is { } pm)
            query = query.Where(a => a.ApplicablePayMonth == pm);
        if (filter.PayYear is { } py)
            query = query.Where(a => a.ApplicablePayYear == py);
        if (filter.EmployeeId is { } eid)
            query = query.Where(a => a.EmployeeId == eid);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(a => a.ApplicablePayYear)
            .ThenByDescending(a => a.ApplicablePayMonth)
            .ThenByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var items = rows
            .Select(a => ToDto(a, employees.GetValueOrDefault(a.EmployeeId), negativeNetWarning: false))
            .ToList();

        return Result<PayrollAdjustmentPageDto>.Success(
            new PayrollAdjustmentPageDto(items, total, page, pageSize));
    }

    public async Task<Result<PayrollAdjustmentDto>> GetAsync(Guid adjustmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollAdjustmentDto>.Failure("Tenant context is not resolved.", 400);

        var adj = await _dbContext.PayrollAdjustments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == adjustmentId, cancellationToken);
        if (adj is null)
            return Result<PayrollAdjustmentDto>.Failure("Adjustment not found.", 404, "adjustment_not_found");

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == adj.EmployeeId, cancellationToken);

        return Result<PayrollAdjustmentDto>.Success(ToDto(adj, employee, negativeNetWarning: false));
    }

    // ── FR-2: bulk CSV ────────────────────────────────────────────────────────

    public async Task<Result<BulkAdjustmentResultDto>> BulkCreateAsync(
        int payMonth, int payYear, Stream csvContent, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BulkAdjustmentResultDto>.Failure("Tenant context is not resolved.", 400);
        if (payMonth is < 1 or > 12)
            return Result<BulkAdjustmentResultDto>.Failure("Pay month must be between 1 and 12.", 400, "invalid_period");
        if (payYear is < 2000 or > 2100)
            return Result<BulkAdjustmentResultDto>.Failure("Pay year must be a valid year.", 400, "invalid_period");

        List<CsvRow> rows;
        try
        {
            rows = ParseCsv(csvContent);
        }
        catch (Exception ex)
        {
            return Result<BulkAdjustmentResultDto>.Failure($"Could not parse CSV: {ex.Message}", 400, "csv_parse_error");
        }

        if (rows.Count == 0)
            return Result<BulkAdjustmentResultDto>.Failure("CSV contained no data rows.", 400, "csv_empty");

        // BR-7/BR-8: the whole batch targets one period; retarget once.
        var (targetYear, targetMonth, _) = await ResolveTargetPeriodAsync(payYear, payMonth, cancellationToken);

        // Look up all referenced employees + their active-salary status once (NFR-2: one query).
        var employeeNos = rows.Select(r => r.EmployeeNo).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => employeeNos.Contains(e.EmployeeNo))
            .ToDictionaryAsync(e => e.EmployeeNo, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var withSalary = await ActiveSalaryEmployeeIdsAsync(employees.Values.Select(e => e.Id).ToList(), cancellationToken);

        var createdBy = _currentUser.UserId.ToString();
        var now = DateTime.UtcNow;
        var results = new List<BulkAdjustmentRowResult>(rows.Count);
        var toAdd = new List<PayrollAdjustment>();

        foreach (var row in rows)
        {
            var (ok, error, errorCode) = ValidateBulkRow(row, employees, withSalary, out var emp, out var type);
            if (!ok)
            {
                results.Add(new BulkAdjustmentRowResult(row.RowNumber, row.EmployeeNo, false, null, error, errorCode));
                continue;
            }

            var adj = new PayrollAdjustment
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = emp!.Id,
                AdjustmentType = type,
                Amount = row.Amount,
                Description = row.Description,
                ApplicablePayMonth = targetMonth,
                ApplicablePayYear = targetYear,
                IsTaxable = row.IsTaxable,
                IsRecurring = false,
                Status = AdjustmentStatus.Pending,
                CreatedBy = createdBy,
                CreatedAt = now,
            };
            toAdd.Add(adj);
            results.Add(new BulkAdjustmentRowResult(row.RowNumber, row.EmployeeNo, true, adj.Id, null, null));
        }

        if (toAdd.Count > 0)
        {
            _dbContext.PayrollAdjustments.AddRange(toAdd);
            await _dbContext.SaveChangesAsync(cancellationToken); // NFR-2: one persist for the batch.
        }

        var succeeded = results.Count(r => r.Success);
        return Result<BulkAdjustmentResultDto>.Success(
            new BulkAdjustmentResultDto(rows.Count, succeeded, rows.Count - succeeded, results));
    }

    // ── AC-3 / NFR-5: document upload + download ───────────────────────────────

    public async Task<Result<string>> UploadDocumentAsync(
        Guid adjustmentId, Stream content, string fileName, string contentType, long length,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<string>.Failure("Tenant context is not resolved.", 400);

        if (length <= 0)
            return Result<string>.Failure("A non-empty file is required.", 400, "file_required");
        if (length > MaxDocumentBytes)
            return Result<string>.Failure("Supporting document exceeds the 5 MB limit.", 400, "file_too_large");

        var ext = Path.GetExtension(fileName);
        if (!AllowedContentTypes.Contains(contentType) || !AllowedExtensions.Contains(ext))
            return Result<string>.Failure("Only PDF, JPG, and PNG files are allowed.", 400, "unsupported_file_type");

        var adj = await _dbContext.PayrollAdjustments.FirstOrDefaultAsync(a => a.Id == adjustmentId, cancellationToken);
        if (adj is null)
            return Result<string>.Failure("Adjustment not found.", 404, "adjustment_not_found");

        // AC-3: store at {tenantId}/payroll/adjustments/{adjustmentId}/ — IFileStorage prefixes {tenantId}/,
        // so the within-tenant path is payroll/adjustments/{adjustmentId}/{fileName}. The file name is
        // sanitized to a safe charset; the path is GUID-derived so traversal is structurally impossible.
        var safeName = SanitizeFileName(fileName);
        var relativePath = $"payroll/adjustments/{adjustmentId}/{safeName}";

        await _fileStorage.UploadAsync(_tenantContext.TenantId, relativePath, content, contentType, cancellationToken);

        adj.SupportingDocumentPath = relativePath;
        adj.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(relativePath);
    }

    public async Task<Result<AdjustmentDocumentDto>> DownloadDocumentAsync(
        Guid adjustmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AdjustmentDocumentDto>.Failure("Tenant context is not resolved.", 400);

        var adj = await _dbContext.PayrollAdjustments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == adjustmentId, cancellationToken);
        if (adj is null)
            return Result<AdjustmentDocumentDto>.Failure("Adjustment not found.", 404, "adjustment_not_found");
        if (string.IsNullOrEmpty(adj.SupportingDocumentPath))
            return Result<AdjustmentDocumentDto>.Failure("No supporting document for this adjustment.", 404, "document_not_found");

        await using var stream = await _fileStorage.OpenReadAsync(_tenantContext.TenantId, adj.SupportingDocumentPath, cancellationToken);
        if (stream is null)
            return Result<AdjustmentDocumentDto>.Failure("Supporting document not found in storage.", 404, "document_not_found");

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);

        var fileName = Path.GetFileName(adj.SupportingDocumentPath);
        var contentType = ContentTypeFor(Path.GetExtension(fileName));
        return Result<AdjustmentDocumentDto>.Success(new AdjustmentDocumentDto(ms.ToArray(), contentType, fileName));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>BR-1: the employee has at least one currently-effective salary component row.</summary>
    private async Task<bool> HasActiveSalaryAsync(Guid employeeId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _dbContext.EmployeeSalaryComponents.AsNoTracking()
            .AnyAsync(c => c.EmployeeId == employeeId
                && c.EffectiveFrom <= today
                && (c.EffectiveTo == null || c.EffectiveTo >= today), ct);
    }

    /// <summary>BR-1 (bulk): which of the given employee ids have a currently-effective salary component.</summary>
    private async Task<HashSet<Guid>> ActiveSalaryEmployeeIdsAsync(List<Guid> employeeIds, CancellationToken ct)
    {
        if (employeeIds.Count == 0) return new HashSet<Guid>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ids = await _dbContext.EmployeeSalaryComponents.AsNoTracking()
            .Where(c => employeeIds.Contains(c.EmployeeId)
                && c.EffectiveFrom <= today
                && (c.EffectiveTo == null || c.EffectiveTo >= today))
            .Select(c => c.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    /// <summary>
    /// BR-7/BR-8: returns the first period at or after the requested one whose run is neither Finalized nor
    /// Processing — i.e. the next available period. A period with NO run, or a run in Queued/ReviewPending/
    /// Approved/Cancelled, is available (the adjustment will be picked up on the run/re-run). The third tuple
    /// element is true when the period was moved.
    /// </summary>
    private async Task<(int Year, int Month, bool Deferred)> ResolveTargetPeriodAsync(
        int year, int month, CancellationToken ct)
    {
        int y = year, m = month, hops = 0;
        var deferred = false;
        while (hops++ < 240) // hard bound (20 years) — defensive.
        {
            var run = await _dbContext.PayrollRuns.AsNoTracking()
                .Where(r => r.PayYear == y && r.PayMonth == m
                    && r.Status != PayrollRunStatus.Cancelled)
                .OrderByDescending(r => r.InitiatedAt)
                .FirstOrDefaultAsync(ct);

            // Available when there is no active run, or the active run hasn't locked the period yet.
            if (run is null
                || (run.Status != PayrollRunStatus.Finalized && run.Status != PayrollRunStatus.Processing))
                return (y, m, deferred);

            // BR-7 (Finalized) or BR-8 (Processing): move to the next period.
            deferred = true;
            (y, m) = NextPeriod(y, m);
        }
        return (y, m, deferred);
    }

    /// <summary>
    /// BR-3: would this deduction drive the employee's net negative for the target period? Estimated against
    /// the period's persisted slip (if a run already produced one); otherwise — for a not-yet-run/future period
    /// (BUG-074) — against the net PROJECTED from the employee's current salary structure, so the advisory is
    /// reachable before a run exists. Both estimates are then net of the OTHER adjustments already Pending for
    /// the period. Non-blocking — surfaced as a warning (BUG-062: negative net can be legitimate). Returns
    /// false only when there is no slip AND no current salary structure to project from.
    /// </summary>
    private async Task<bool> WouldDriveNetNegativeAsync(
        Guid employeeId, int year, int month, decimal newDeduction, CancellationToken ct)
    {
        decimal projectedNet;

        var slip = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId && s.PayYear == year && s.PayMonth == month)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (slip is not null)
        {
            projectedNet = slip.NetSalary;
        }
        else
        {
            // BUG-074: no computed slip yet (normal future-period adjustment). Project the net from the
            // employee's CURRENT salary components so the warning can still fire. Null = no structure to
            // estimate against → cannot warn.
            var baseline = await ProjectCurrentStructureNetAsync(employeeId, year, month, ct);
            if (baseline is null)
                return false;
            projectedNet = baseline.Value;
        }

        // Net effect of OTHER Pending adjustments already queued for this period (earnings add, deductions subtract).
        var pending = await _dbContext.PayrollAdjustments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId
                && a.ApplicablePayYear == year && a.ApplicablePayMonth == month
                && a.Status == AdjustmentStatus.Pending)
            .ToListAsync(ct);

        foreach (var a in pending)
            projectedNet += a.AdjustmentType == AdjustmentType.Deduction ? -a.Amount : a.Amount;
        projectedNet -= newDeduction;

        return projectedNet < 0m;
    }

    /// <summary>
    /// BUG-074: projects the net a full-month run would produce for the employee from their CURRENT salary
    /// structure (the components effective today), reusing the same row→input mapping
    /// (<see cref="PayrollRunProcessor.BuildComponentInputs"/>) and the pure calculation engine
    /// (<see cref="PayrollSlipCalculator"/>) the run itself uses — no gross/deduction maths is re-implemented
    /// here. A plain baseline: full month, no LOP, no pro-ration (the advisory only estimates the structure).
    /// NOTE (accuracy): this baseline deliberately OMITS the statutory deductions (EPF/ETF/tax) the run applies
    /// AFTER Compute, so the projected net is an UPPER bound on the run's actual net. The advisory is therefore
    /// CONSERVATIVE — it never false-warns, but can under-warn when statutory load alone would drive net negative.
    /// Acceptable for a non-blocking advisory; running statutory in the projection is a documented follow-up.
    /// Returns null when the employee has no current salary structure.
    /// </summary>
    private async Task<decimal?> ProjectCurrentStructureNetAsync(
        Guid employeeId, int year, int month, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await _dbContext.EmployeeSalaryComponents.AsNoTracking()
            .Where(c => c.EmployeeId == employeeId
                && c.EffectiveFrom <= today
                && (c.EffectiveTo == null || c.EffectiveTo >= today))
            .ToListAsync(ct);
        if (rows.Count == 0)
            return null;

        var componentIds = rows.Select(r => r.SalaryComponentId).Distinct().ToList();
        var meta = await _dbContext.SalaryComponents.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var inputs = PayrollRunProcessor.BuildComponentInputs(rows, meta);

        // Full-month baseline: no LOP, no pro-ration. lopComponentId is unused when LopDays == 0.
        var workingDays = DateTime.DaysInMonth(year, month);
        var input = new PayrollSlipInput(employeeId, inputs, workingDays, 0m, null);
        var result = PayrollSlipCalculator.Compute(input, Guid.Empty);
        return result.NetSalary;
    }

    private PayrollAdjustment NewAdjustment(
        CreateAdjustmentInput input, AdjustmentType type, int year, int month,
        Guid? seriesId, string createdBy, DateTime now) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        EmployeeId = input.EmployeeId,
        AdjustmentType = type,
        Amount = input.Amount,
        Description = input.Description,
        ApplicablePayMonth = month,
        ApplicablePayYear = year,
        IsTaxable = input.IsTaxable,
        IsRecurring = input.IsRecurring,
        RecurrenceEndMonth = input.IsRecurring ? input.RecurrenceEndMonth : null,
        RecurrenceEndYear = input.IsRecurring ? input.RecurrenceEndYear : null,
        Status = AdjustmentStatus.Pending,
        ReferencePayrollSlipId = type == AdjustmentType.Correction ? input.ReferencePayrollSlipId : null,
        RecurringSeriesId = seriesId,
        CreatedBy = createdBy,
        CreatedAt = now,
    };

    private static (int Year, int Month) NextPeriod(int year, int month)
        => month == 12 ? (year + 1, 1) : (year, month + 1);

    private static PayrollAdjustmentDto ToDto(PayrollAdjustment a, Employee? emp, bool negativeNetWarning) => new(
        a.Id,
        a.EmployeeId,
        emp?.EmployeeNo ?? string.Empty,
        emp is null ? string.Empty : $"{emp.FirstName} {emp.LastName}".Trim(),
        a.AdjustmentType.ToString(),
        a.Amount,
        a.Description,
        a.ApplicablePayMonth,
        a.ApplicablePayYear,
        a.IsTaxable,
        a.IsRecurring,
        a.RecurrenceEndMonth,
        a.RecurrenceEndYear,
        a.Status.ToString(),
        a.AppliedInPayrollRunId,
        a.ReferencePayrollSlipId,
        !string.IsNullOrEmpty(a.SupportingDocumentPath),
        a.RecurringSeriesId,
        a.CreatedAt,
        negativeNetWarning);

    // ── CSV parsing (FR-2) ───────────────────────────────────────────────────

    private sealed record CsvRow(int RowNumber, string EmployeeNo, string RawType, decimal Amount, string Description, bool IsTaxable);

    /// <summary>
    /// Parses the bulk CSV (header required): employee_no, adjustment_type, amount, description, is_taxable.
    /// Tolerant of quoted fields and surrounding whitespace. Malformed amount/type rows are kept (RawType
    /// preserved) so per-row validation reports them rather than failing the whole upload.
    /// </summary>
    private static List<CsvRow> ParseCsv(Stream content)
    {
        using var reader = new StreamReader(content);
        var rows = new List<CsvRow>();
        string? line;
        var lineNo = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (lineNo == 1) continue; // header row.
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = SplitCsvLine(line);
            var empNo = Field(fields, 0);
            var rawType = Field(fields, 1);
            var rawAmount = Field(fields, 2);
            var description = Field(fields, 3);
            var rawTaxable = Field(fields, 4);

            decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);
            var taxable = rawTaxable.Equals("true", StringComparison.OrdinalIgnoreCase)
                || rawTaxable == "1" || rawTaxable.Equals("yes", StringComparison.OrdinalIgnoreCase);

            rows.Add(new CsvRow(lineNo, empNo, rawType, amount, description, taxable));
        }
        return rows;
    }

    private static (bool Ok, string? Error, string? ErrorCode) ValidateBulkRow(
        CsvRow row, IReadOnlyDictionary<string, Employee> employees, HashSet<Guid> withSalary,
        out Employee? emp, out AdjustmentType type)
    {
        emp = null;
        type = default;

        if (string.IsNullOrWhiteSpace(row.EmployeeNo))
            return (false, "Employee number is required.", "employee_no_required");
        if (!employees.TryGetValue(row.EmployeeNo, out var found))
            return (false, $"Employee '{row.EmployeeNo}' not found.", "employee_not_found");
        if (!Enum.TryParse(row.RawType, ignoreCase: true, out type) || !Enum.IsDefined(type))
            return (false, $"Invalid adjustment type '{row.RawType}'.", "invalid_adjustment_type");
        if (type == AdjustmentType.Correction)
            return (false, "Correction adjustments are not supported in bulk upload.", "correction_not_bulk");
        if (row.Amount <= 0m)
            return (false, "Amount must be greater than zero.", "invalid_amount");
        if (string.IsNullOrWhiteSpace(row.Description))
            return (false, "Description is required.", "description_required");
        if (!withSalary.Contains(found.Id))
            return (false, "Employee does not have an active salary assignment.", "no_active_salary");

        emp = found;
        return (true, null, null);
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string Field(string[] fields, int index)
        => index < fields.Length ? fields[index].Trim() : string.Empty;

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "document" : clean;
    }

    private static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };
}
