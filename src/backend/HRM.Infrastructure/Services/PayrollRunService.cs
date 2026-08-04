using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PA = HRM.Domain.Payroll.PayrollAuditAction;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Payroll-run service (US-PAY-003) — the INITIATE + READ side. All queries are tenant-scoped via
/// ITenantContext + the EF global query filter (AC-7). Creates the queued run, enqueues the Hangfire
/// processing job via the optional <see cref="IPayrollRunJobScheduler"/> seam (FR-2), enforces one
/// non-cancelled run per period (BR-1) + the already-finalized rejection (AC-4) + idempotency (FR-9), and
/// exposes the run list/detail/summary/progress reads (FR-6/FR-8).
///
/// <para>LOCKING (NFR-3): no distributed-lock infra exists; the documented choice is the partial unique
/// index <c>ix_payroll_run_one_active_per_period</c> on (tenant, year, month) for non-cancelled runs, plus
/// an in-service pre-check for a fast, friendly 409. A concurrent racing insert is caught by the unique
/// index (DbUpdateException) and surfaced as the same 409.</para>
/// </summary>
public sealed class PayrollRunService : IPayrollRunService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IAttendancePayrollService _attendancePayroll;
    private readonly IPayrollAuditLogger _audit;
    private readonly IPayrollSlipCleaner _slipCleaner;
    private readonly IPayrollRunJobScheduler? _jobScheduler;
    private readonly ILogger<PayrollRunService> _logger;

    private static readonly string[] MonthNames =
    {
        string.Empty, "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    public PayrollRunService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IAttendancePayrollService attendancePayroll,
        IPayrollAuditLogger audit,
        IPayrollSlipCleaner slipCleaner,
        ILogger<PayrollRunService> logger,
        IPayrollRunJobScheduler? jobScheduler = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _attendancePayroll = attendancePayroll;
        _audit = audit;
        _slipCleaner = slipCleaner;
        _logger = logger;
        _jobScheduler = jobScheduler;
    }

    public async Task<Result<PayrollRunAcceptedDto>> InitiateAsync(InitiatePayrollRunInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunAcceptedDto>.Failure("Tenant context is not resolved.", 400);

        if (input.PayMonth is < 1 or > 12)
            return Result<PayrollRunAcceptedDto>.Failure("Pay month must be between 1 and 12.", 400, "invalid_month");

        // FR-9: a re-used idempotency key returns the existing run (idempotent — no duplicate run created).
        if (!string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            var existingByKey = await _dbContext.PayrollRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IdempotencyKey == input.IdempotencyKey, cancellationToken);
            if (existingByKey is not null)
            {
                // ISSUE-153 / FR-9: replaying the SAME idempotency key for the SAME period is IDEMPOTENT — return
                // the existing run as a success (not a 409), so a retried/duplicated request gets the same run
                // back rather than an error, and never spawns a second run.
                if (existingByKey.PayYear == input.PayYear && existingByKey.PayMonth == input.PayMonth)
                    return Result<PayrollRunAcceptedDto>.Success(new PayrollRunAcceptedDto
                    {
                        RunId = existingByKey.Id,
                        Status = existingByKey.Status.ToString(),
                        PayMonth = existingByKey.PayMonth,
                        PayYear = existingByKey.PayYear,
                    });

                // The key was already used for a DIFFERENT period — a client error (an idempotency key must
                // identify one request). Reject rather than silently return the wrong period's run.
                return Result<PayrollRunAcceptedDto>.Failure(
                    "A payroll run with this idempotency key already exists.", 409, "duplicate_idempotency_key");
            }
        }

        // BR-1 / AC-4: only one non-cancelled run per (tenant, year, month). An already-Finalized run for the
        // period is the AC-4 case (distinct error code so the FE can message "already finalized").
        var existingForPeriod = await _dbContext.PayrollRuns.AsNoTracking()
            .Where(r => r.PayYear == input.PayYear && r.PayMonth == input.PayMonth && r.Status != PayrollRunStatus.Cancelled)
            .OrderByDescending(r => r.InitiatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingForPeriod is not null)
        {
            return existingForPeriod.Status == PayrollRunStatus.Finalized
                ? Result<PayrollRunAcceptedDto>.Failure(
                    $"Payroll for {input.PayYear:D4}-{input.PayMonth:D2} is already finalized.", 409, "period_already_finalized")
                : Result<PayrollRunAcceptedDto>.Failure(
                    $"A payroll run for {input.PayYear:D4}-{input.PayMonth:D2} is already in progress.", 409, "run_in_progress");
        }

        // US-PAY-010 AC-4/FR-6: BLOCK initiate when attendance for the period is NOT finalized/locked. The
        // attendance PERIOD LOCK (US-ATT-009) is the "finalized" signal — HR locks the period before payroll so
        // retroactive attendance edits can't invalidate the computed slips. Without it, LOP/overtime cannot be
        // trusted, so we fail-closed with a clear, FE-friendly warning rather than running on provisional data.
        if (!await IsAttendanceFinalizedAsync(input.PayYear, input.PayMonth, cancellationToken))
        {
            var monthName = input.PayMonth is >= 1 and <= 12 ? MonthNames[input.PayMonth] : input.PayMonth.ToString();
            return Result<PayrollRunAcceptedDto>.Failure(
                $"Attendance data for {monthName} {input.PayYear} is not yet finalized.", 409, "attendance_not_finalized");
        }

        var now = DateTime.UtcNow;
        var run = new PayrollRun
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            PayMonth = input.PayMonth,
            PayYear = input.PayYear,
            Status = PayrollRunStatus.Queued,
            InitiatedBy = _currentUser.UserId,
            InitiatedAt = now,
            IdempotencyKey = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? null : input.IdempotencyKey.Trim(),
            IsDeleted = false,
        };

        _dbContext.PayrollRuns.Add(run);

        // US-PAY-012 (FR-2): audit the run initiation — staged into the SAME SaveChanges so a unique-index
        // conflict (BR-1) discards the audit entry too (no audit row for a run that was never created).
        _audit.Log(PA.PayrollRunInitiated, PA.ResourceType.PayrollRun,
            run.Id.ToString(), before: null,
            after: new { run.PayMonth, run.PayYear, Status = run.Status.ToString(), run.InitiatedBy });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent racing insert tripped the partial unique index (BR-1 / FR-9). Treat as 409.
            _logger.LogWarning(ex,
                "Payroll run initiate conflicted on the unique index. Tenant={TenantId}, Period={Year}-{Month}",
                _tenantContext.TenantId, input.PayYear, input.PayMonth);
            return Result<PayrollRunAcceptedDto>.Failure(
                $"A payroll run for {input.PayYear:D4}-{input.PayMonth:D2} is already in progress.", 409, "run_in_progress");
        }

        // FR-2/FR-3: enqueue the tenant-aware processing job (when the Hangfire-backed scheduler is registered).
        if (_jobScheduler is not null)
        {
            _jobScheduler.Enqueue(_tenantContext.TenantId, _tenantContext.Subdomain, run.Id);
        }
        else
        {
            _logger.LogInformation(
                "Payroll run {RunId} queued but no IPayrollRunJobScheduler is registered — processing must be triggered directly (dev/test).",
                run.Id);
        }

        _logger.LogInformation(
            "Payroll run initiated. RunId={RunId}, Period={Year}-{Month}, Tenant={TenantId}, By={User}",
            run.Id, input.PayYear, input.PayMonth, _tenantContext.TenantId, _currentUser.Email);

        return Result<PayrollRunAcceptedDto>.Success(new PayrollRunAcceptedDto
        {
            RunId = run.Id,
            Status = run.Status.ToString(),
            PayMonth = run.PayMonth,
            PayYear = run.PayYear,
        });
    }

    public async Task<Result<PayrollRunAcceptedDto>> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunAcceptedDto>.Failure("Tenant context is not resolved.", 400);

        // Tracked load (we mutate Status). Tenant-scoped by the global filter — a cross-tenant run is invisible
        // (returns 404, never leaks cross-tenant existence).
        var run = await _dbContext.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayrollRunAcceptedDto>.Failure("Payroll run not found.", 404, "run_not_found");

        // BR-7: a Finalized run is immutable — corrections go through a payroll adjustment, not a cancel.
        if (run.Status == PayrollRunStatus.Finalized)
            return Result<PayrollRunAcceptedDto>.Failure(
                "A finalized payroll run cannot be cancelled.", 409, "run_finalized");

        // Idempotency is NOT assumed a no-op success — a repeat cancel is surfaced as a clear 409 (mirrors the
        // approval-service invalid-transition 409) so the caller knows nothing changed.
        if (run.Status == PayrollRunStatus.Cancelled)
            return Result<PayrollRunAcceptedDto>.Failure(
                "This payroll run is already cancelled.", 409, "run_already_cancelled");

        // ISSUE-154 (durability): a run actively being computed cannot be cancelled MID-FLIGHT. The
        // ProcessAsync Cancelled guard only fires at START — a job already PAST that guard (in Processing) would,
        // on completion, re-insert slips and flip the run back to ReviewPending, silently discarding the cancel
        // (there is no concurrency token → last-writer-wins). So reject Processing with 409 and let HR cancel
        // once it reaches ReviewPending. A Queued run stays cancellable: its enqueued job hits the start-guard.
        if (run.Status == PayrollRunStatus.Processing)
            return Result<PayrollRunAcceptedDto>.Failure(
                "This payroll run is still processing and cannot be cancelled until it is ready for review.", 409, "run_in_progress");

        var previousStatus = run.Status;

        // Clean up: remove the run's slips + revert its Applied adjustments to Pending — the SAME shared cleanup
        // the re-run path uses (ISSUE-154). This frees the partial-unique-index slot so HR can initiate a fresh
        // run for the same period. Note: this does NOT touch the US-ATT-009 attendance period lock (payroll
        // never holds it — that stays an attendance-domain concern).
        await _slipCleaner.RemoveRunSlipsAndRevertAdjustmentsAsync(run.Id, cancellationToken);

        run.Status = PayrollRunStatus.Cancelled;

        // US-PAY-012: audit the cancellation as the acting HR user (an HTTP action, not a system-job write).
        _audit.Log(PA.PayrollRunCancelled, PA.ResourceType.PayrollRun,
            run.Id.ToString(),
            before: new { Status = previousStatus.ToString() },
            after: new { run.PayYear, run.PayMonth, Status = run.Status.ToString() });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payroll run cancelled. RunId={RunId}, Period={Year}-{Month}, From={From}, Tenant={TenantId}, By={User}",
            run.Id, run.PayYear, run.PayMonth, previousStatus, _tenantContext.TenantId, _currentUser.Email);

        return Result<PayrollRunAcceptedDto>.Success(new PayrollRunAcceptedDto
        {
            RunId = run.Id,
            Status = run.Status.ToString(),
            PayMonth = run.PayMonth,
            PayYear = run.PayYear,
        });
    }

    public async Task<Result<PayrollRunAcceptedDto>> RerunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunAcceptedDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayrollRunAcceptedDto>.Failure("Payroll run not found.", 404, "run_not_found");

        // ISSUE-154 guard matrix — re-run reuses the SAME run in place; only a processed-but-not-approved run
        // (ReviewPending) is safe to reprocess. Everything else is a distinct 409.
        switch (run.Status)
        {
            case PayrollRunStatus.Finalized:
                return Result<PayrollRunAcceptedDto>.Failure(
                    "A finalized payroll run cannot be re-run.", 409, "run_finalized");
            case PayrollRunStatus.Queued:
            case PayrollRunStatus.Processing:
                return Result<PayrollRunAcceptedDto>.Failure(
                    "This payroll run is still processing and cannot be re-run.", 409, "run_in_progress");
            case PayrollRunStatus.Cancelled:
                return Result<PayrollRunAcceptedDto>.Failure(
                    "A cancelled payroll run cannot be re-run — initiate a new run for the period.", 409, "run_cancelled");
            case PayrollRunStatus.ReviewPending:
                break; // the only re-runnable state.
            default:
                // AwaitingApproval / Approved / Rejected — return the run to HR (ReviewPending) before re-running.
                //
                // DELIBERATE ASYMMETRY with the worker-side guard (PayrollRunProcessor
                // .GuardNonReprocessableStatus): this REQUEST-time gate refuses Rejected too, while the worker
                // still processes a Rejected run. That is intentional, not drift. Here we are answering "may HR
                // start a re-run right now?" — and the answer for a rejected run is "submit the correction
                // first", which is clearer than silently re-running. The worker faces a different question: a
                // reprocess that was ALREADY legitimately requested has arrived late, and refusing it there
                // would strand work rather than prevent it. The worker only guards the states where proceeding
                // would destroy something (an in-flight approval, or signed-off figures); a Rejected run has
                // neither.
                return Result<PayrollRunAcceptedDto>.Failure(
                    $"A payroll run in status {run.Status} cannot be re-run.", 409, "run_not_rerunnable");
        }

        // US-PAY-012: audit the re-run request as the acting HR user. Committed FIRST (commit-then-enqueue,
        // matching InitiateAsync) so we never enqueue a reprocess job whose audit row failed to persist.
        _audit.Log(PA.PayrollRunReprocessed, PA.ResourceType.PayrollRun,
            run.Id.ToString(),
            before: new { Status = run.Status.ToString() },
            after: new { run.PayYear, run.PayMonth, Status = run.Status.ToString() });

        // DF-61: mark that a reprocess was requested (committed WITH the audit row, before the best-effort
        // enqueue below). The run stays ReviewPending; this marker is what lets PayrollRunReconcileJob tell a
        // "reprocess requested but the enqueue was dropped" run apart from a correctly-processed ReviewPending
        // one, so a lost enqueue self-heals. Cleared by PayrollRunProcessor.ProcessAsync on completion.
        run.ReprocessRequestedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Re-enqueue the tenant-aware processing job AFTER the commit (which re-invokes ProcessAsync — its own
        // shared cleanup replaces the prior slips, FR-7). We do NOT call ProcessAsync inline on the request
        // thread. When no scheduler is registered (dev/test), processing is driven directly by the caller.
        if (_jobScheduler is not null)
        {
            _jobScheduler.Enqueue(_tenantContext.TenantId, _tenantContext.Subdomain, run.Id);
        }
        else
        {
            _logger.LogInformation(
                "Payroll run {RunId} re-run requested but no IPayrollRunJobScheduler is registered — processing must be triggered directly (dev/test).",
                run.Id);
        }

        _logger.LogInformation(
            "Payroll run re-run requested. RunId={RunId}, Period={Year}-{Month}, Tenant={TenantId}, By={User}",
            run.Id, run.PayYear, run.PayMonth, _tenantContext.TenantId, _currentUser.Email);

        return Result<PayrollRunAcceptedDto>.Success(new PayrollRunAcceptedDto
        {
            RunId = run.Id,
            Status = run.Status.ToString(),
            PayMonth = run.PayMonth,
            PayYear = run.PayYear,
        });
    }

    public async Task<Result<IReadOnlyList<PayrollRunDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayrollRunDto>>.Failure("Tenant context is not resolved.", 400);

        var runs = await _dbContext.PayrollRuns.AsNoTracking()
            .OrderByDescending(r => r.PayYear).ThenByDescending(r => r.PayMonth).ThenByDescending(r => r.InitiatedAt)
            .Select(r => Map(r))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PayrollRunDto>>.Success(runs);
    }

    public async Task<Result<PayrollRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        return run is null
            ? Result<PayrollRunDto>.Failure("Payroll run not found.", 404, "run_not_found")
            : Result<PayrollRunDto>.Success(Map(run));
    }

    public async Task<Result<PayrollRunSummaryDto>> GetSummaryAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunSummaryDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        return run is null
            ? Result<PayrollRunSummaryDto>.Failure("Payroll run not found.", 404, "run_not_found")
            : Result<PayrollRunSummaryDto>.Success(new PayrollRunSummaryDto { Run = Map(run), RunLog = run.RunLog });
    }

    public async Task<Result<PayrollRunProgressDto>> GetProgressAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunProgressDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayrollRunProgressDto>.Failure("Payroll run not found.", 404, "run_not_found");

        var complete = run.Status is not (PayrollRunStatus.Queued or PayrollRunStatus.Processing);

        return Result<PayrollRunProgressDto>.Success(new PayrollRunProgressDto
        {
            RunId = run.Id,
            Status = run.Status.ToString(),
            TotalEmployees = run.TotalEmployees,
            ProcessedEmployees = run.ProcessedEmployees,
            SkippedEmployees = run.SkippedEmployees,
            IsComplete = complete,
        });
    }

    public async Task<Result<PrePayrollReconciliationDto>> GetPrePayrollReconciliationAsync(
        int payYear, int payMonth, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PrePayrollReconciliationDto>.Failure("Tenant context is not resolved.", 400);
        if (payMonth is < 1 or > 12)
            return Result<PrePayrollReconciliationDto>.Failure("Pay month must be between 1 and 12.", 400, "invalid_month");

        // Active employees (tenant-scoped by the global filter — FR-8). Mirror the run engine's active set.
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.IsActive
                && (e.Status == EmployeeStatus.Active || e.Status == EmployeeStatus.Probation))
            .OrderBy(e => e.EmployeeNo)
            .ToListAsync(cancellationToken);
        var employeeIds = employees.Select(e => e.Id).ToList();

        // FR-7: reuse the US-ATT-009 attendance pull for working/present/absent/LOP/overtime (best-effort —
        // no rows when attendance is unavailable; the report still renders the leave + employee columns).
        var attendanceByEmployee = new Dictionary<Guid, AttendancePayrollRowDto>();
        try
        {
            var pull = await _attendancePayroll.GetPayrollDataAsync(payYear, payMonth, employeeIds, cancellationToken);
            if (pull.IsSuccess && pull.Value is not null)
                attendanceByEmployee = pull.Value.Rows.ToDictionary(r => r.EmployeeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconciliation attendance pull failed for {Year}-{Month}.", payYear, payMonth);
        }

        // FR-2/FR-7: approved-leave days for the period, grouped by employee + leave-type name (tenant-scoped).
        var leaveByEmployee = await ApprovedLeaveByEmployeeAsync(payYear, payMonth, employeeIds, cancellationToken);

        var finalized = await IsAttendanceFinalizedAsync(payYear, payMonth, cancellationToken);
        var workingDaysFallback = WorkingDaysInMonth(payYear, payMonth);

        var rows = new List<PrePayrollReconciliationRowDto>(employees.Count);
        foreach (var emp in employees)
        {
            var att = attendanceByEmployee.GetValueOrDefault(emp.Id);
            var leave = leaveByEmployee.GetValueOrDefault(emp.Id) ?? new Dictionary<string, decimal>();

            rows.Add(new PrePayrollReconciliationRowDto
            {
                EmployeeId = emp.Id,
                EmployeeNo = emp.EmployeeNo,
                EmployeeName = $"{emp.FirstName} {emp.LastName}".Trim(),
                WorkingDays = att?.TotalWorkingDays ?? workingDaysFallback,
                PresentDays = att?.TotalPresentDays ?? 0m,
                AbsentDays = att?.TotalAbsentDays ?? 0m,
                LeaveDaysByType = leave,
                TotalLeaveDays = leave.Values.Sum(),
                OvertimeHours = att is null ? 0m : Math.Round(att.ApprovedOvertimeMinutes / 60m, 2, MidpointRounding.AwayFromZero),
                CalculatedLopDays = att?.LopDays ?? 0m,
            });
        }

        return Result<PrePayrollReconciliationDto>.Success(new PrePayrollReconciliationDto
        {
            Period = $"{payYear:D4}-{payMonth:D2}",
            PayMonth = payMonth,
            PayYear = payYear,
            AttendanceFinalized = finalized,
            Rows = rows,
        });
    }

    /// <summary>
    /// FR-2/FR-7: approved-leave days in the period per employee, grouped by leave-type name. A request counts
    /// toward the period when its [start, end] window overlaps the month; full-period TotalDays is attributed
    /// (a finer day-in-month split is a documented follow-up). Tenant-scoped by the global filter.
    /// </summary>
    private async Task<Dictionary<Guid, Dictionary<string, decimal>>> ApprovedLeaveByEmployeeAsync(
        int year, int month, IReadOnlyList<Guid> employeeIds, CancellationToken ct)
    {
        if (employeeIds.Count == 0) return new Dictionary<Guid, Dictionary<string, decimal>>();

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var rows = await (
            from r in _dbContext.LeaveRequests.AsNoTracking()
            join t in _dbContext.LeaveTypes.AsNoTracking() on r.LeaveTypeId equals t.Id
            where r.Status == LeaveRequestStatus.Approved
                && employeeIds.Contains(r.EmployeeId)
                && r.StartDate <= monthEnd && r.EndDate >= monthStart
            select new { r.EmployeeId, TypeName = t.Name, r.TotalDays }).ToListAsync(ct);

        return rows
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.TypeName)
                      .ToDictionary(tg => tg.Key, tg => tg.Sum(x => x.TotalDays)));
    }

    private static int WorkingDaysInMonth(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return end.DayNumber - start.DayNumber + 1;
    }

    /// <summary>
    /// US-PAY-010 AC-4/FR-6: is the attendance period finalized? Reuses the US-ATT-009 period lock — a period is
    /// "finalized" when an ACTIVE lock covers it. A failed lookup is treated as NOT finalized (fail-closed): the
    /// gate exists precisely to prevent running on un-trustworthy attendance, so an unknown state must block.
    /// </summary>
    private async Task<bool> IsAttendanceFinalizedAsync(int year, int month, CancellationToken ct)
    {
        try
        {
            var lockResult = await _attendancePayroll.GetPeriodLockAsync(year, month, ct);
            return lockResult.IsSuccess && lockResult.Value is { IsLocked: true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Attendance-finalized check failed for {Year}-{Month}; blocking payroll initiate (fail-closed).",
                year, month);
            return false;
        }
    }

    private static PayrollRunDto Map(PayrollRun r) => new()
    {
        Id = r.Id,
        PayMonth = r.PayMonth,
        PayYear = r.PayYear,
        Status = r.Status.ToString(),
        TotalEmployees = r.TotalEmployees,
        ProcessedEmployees = r.ProcessedEmployees,
        SkippedEmployees = r.SkippedEmployees,
        TotalGross = r.TotalGross,
        TotalDeductions = r.TotalDeductions,
        TotalNet = r.TotalNet,
        TotalStatutory = r.TotalStatutory,
        InitiatedBy = r.InitiatedBy,
        InitiatedAt = r.InitiatedAt,
        CompletedAt = r.CompletedAt,
        ApprovedBy = r.ApprovedBy,
        ApprovedAt = r.ApprovedAt,
        FinalizedAt = r.FinalizedAt,
    };
}
