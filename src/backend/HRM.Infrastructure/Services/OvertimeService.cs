using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Overtime tracking and approval (US-ATT-006). All queries are tenant-scoped via ITenantContext + the
/// EF global query filters (NFR-2 asks for PostgreSQL RLS, which this codebase does NOT use). Mirrors
/// the single-level direct-report approval pattern of <see cref="RegularizationApprovalService"/>
/// (US-ATT-004): self-approval prevention (BR-8), direct-report authorization, immutable decision
/// history.
///
/// The clock-out auto-detection path (AC-1/FR-1/NFR-1) is exposed as <see cref="BuildAutoDetectedAsync"/>
/// so <see cref="AttendanceService"/> can create the overtime record inside the SAME clock-out
/// transaction (no extra SaveChanges, no extra API call).
///
/// Deferred (flagged, not blocked): Approval Workflow Engine (FR-5, US-ADM-007) — workflow_instance_id
/// stays null; Notifications (FR-8 HR alert / manager / employee, US-NTF) — TODO, not stubbed; Payroll
/// (FR-7, US-ATT-009) — approval sets IsPayrollReady only.
/// </summary>
public sealed class OvertimeService : IOvertimeService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IWorkflowRuntime? _workflowRuntime;
    private readonly IAttendanceNotificationService? _notifications;
    private readonly IHolidayProvider? _holidayProvider;
    private readonly ILogger<OvertimeService> _logger;

    public OvertimeService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<OvertimeService> logger,
        IWorkflowRuntime? workflowRuntime = null,
        IAttendanceNotificationService? notifications = null,
        IHolidayProvider? holidayProvider = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        // US-ADM-011c FR-11: optional so existing US-ATT-006 unit tests keep compiling; DI always supplies it.
        // When a pre-approval is workflow-driven (WorkflowInstanceId set), decisions route through the runtime.
        _workflowRuntime = workflowRuntime;
        // US-NTF-006 Phase 6: optional so existing US-ATT-006 unit tests keep compiling; DI always supplies the Real impl.
        _notifications = notifications;
        // BUG-286: optional for the same reason; DI always supplies HolidayProvider. Without it, holiday
        // lookup falls back to the legacy tenant-wide query, which is NOT location-scoped.
        _holidayProvider = holidayProvider;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-1 / FR-1 / NFR-1 — auto-detection on clock-out (called by AttendanceService)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds (but does NOT save) an <see cref="OvertimeRecord"/> for a just-closed attendance session
    /// when net work exceeds the shift standard by at least the threshold (BR-1/BR-2). Returns null when
    /// no overtime is recognized. The caller (clock-out) adds it to the same change-tracker and commits
    /// it atomically with the attendance_log (NFR-1).
    ///
    /// - Standard hours come from the employee's resolved shift (US-ATT-005), falling back to the tenant
    ///   <see cref="AttendanceSettings.StandardWorkMinutes"/> when there is no shift (AC-1 baseline).
    /// - Multiplier from weekday/weekend/holiday (BR-3/BR-7) using the tenant holiday calendar + day-of-week.
    /// - Capped at <see cref="AttendanceSettings.MaxDailyOvertimeMinutes"/> (BR-4/FR-8 — flagged if capped).
    /// - Weekly cap (BR-5/FR-8): if this record pushes the employee's week over the max, the alert flag is set.
    /// - Status PENDING, or UNAPPROVED when the tenant requires pre-approval and no matching pre-approval
    ///   exists for the date (BR-6) — UNAPPROVED is never payroll-ready.
    /// </summary>
    public async Task<OvertimeRecord?> BuildAutoDetectedAsync(
        AttendanceLog log,
        Employee employee,
        AttendanceSettings settings,
        int netWorkMinutes,
        CancellationToken cancellationToken)
    {
        // BR-1 baseline: the standard from the resolved shift, else the tenant setting.
        var date = DateOnly.FromDateTime(log.ClockIn);
        var standardMinutes = await ResolveStandardMinutesAsync(employee.Id, date, settings, cancellationToken);

        // BR-3/BR-7: multiplier basis — public holiday > weekend > weekday. BUG-286: the holiday lookup is
        // scoped to the employee's location. BUG-285: "weekend" is derived from their RESOLVED work-week.
        var isHoliday = await IsPublicHolidayAsync(date, employee.LocationId, cancellationToken);
        var workingDays = await ResolveWorkingDaysAsync(employee.Id, date, cancellationToken);
        var (multiplier, multiplierBasis) = OvertimeMultiplierResolver.Resolve(
            date, isHoliday,
            settings.WeekdayOvertimeMultiplier,
            settings.WeekendOvertimeMultiplier,
            settings.HolidayOvertimeMultiplier,
            workingDays);

        // FR-1/BR-2/BR-4: detect + cap.
        var computation = AttendanceCalculator.CalculateOvertime(
            netWorkMinutes,
            standardMinutes,
            settings.OvertimeMinimumThresholdMinutes,
            settings.MaxDailyOvertimeMinutes,
            multiplier,
            multiplierBasis);

        if (computation.OvertimeMinutes <= 0)
            return null;   // below threshold or no excess — no overtime record (test hint: 8h20m case).

        // BR-6: when pre-approval is required, look for a matching pre-approval for the date.
        var requirePreApproval = settings.RequireOvertimePreApproval;
        var hasPreApproval = requirePreApproval && await _dbContext.OvertimeRecords
            .AnyAsync(o => o.EmployeeId == employee.Id
                        && o.Date == date
                        && o.Type == OvertimeType.PreApproved,
                      cancellationToken);

        var status = requirePreApproval && !hasPreApproval
            ? OvertimeStatus.Unapproved
            : OvertimeStatus.Pending;

        // BR-5/FR-8: weekly-cap alert — sum the employee's existing approved+pending overtime in the
        // ISO week of this date and flag if adding this block crosses the max.
        var weeklyExceeded = await EvaluateWeeklyCapAsync(
            employee.Id, date, computation.OvertimeMinutes, settings.MaxWeeklyOvertimeMinutes, cancellationToken);

        var record = new OvertimeRecord
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            AttendanceLogId = log.Id,
            Date = date,
            OvertimeMinutes = computation.OvertimeMinutes,
            ApprovedMinutes = null,
            Multiplier = computation.Multiplier,
            Type = OvertimeType.AutoDetected,
            Status = status,
            Reason = "Auto-detected overtime on clock-out.",
            IsPayrollReady = false,
            DailyCapApplied = computation.CapApplied,
            WeeklyCapExceeded = weeklyExceeded,
            CalculationBasis = computation.Basis,
            WorkflowInstanceId = null,   // TODO(US-ADM-007): set when the Approval Workflow Engine lands.
        };

        if (computation.CapApplied || weeklyExceeded)
        {
            // FR-8: alert the attendance-admin/HR pool when daily/weekly maxima are exceeded (US-NTF-006 Phase 6).
            // The flags above persist the condition; the notification service never throws, so a delivery failure
            // cannot break the clock-out that owns this record.
            _logger.LogWarning(
                "Overtime cap exceeded for employee {EmployeeId} on {Date} (tenant {TenantId}): " +
                "dailyCapApplied={DailyCap}, weeklyCapExceeded={WeeklyCap}.",
                employee.Id, date, _tenantContext.TenantId, computation.CapApplied, weeklyExceeded);

            if (_notifications is not null)
            {
                // Weekly breach takes precedence when both trip; report the exceeded period + its configured limit.
                var (period, limitMinutes) = weeklyExceeded
                    ? ("weekly", settings.MaxWeeklyOvertimeMinutes)
                    : ("daily", settings.MaxDailyOvertimeMinutes);
                await _notifications.NotifyOvertimeMaximaExceededAsync(
                    employee.Id,
                    computation.OvertimeMinutes / 60m,
                    limitMinutes / 60m,
                    period,
                    cancellationToken);
            }
        }

        return record;
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-2 / FR-4 — pre-approval submit
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OvertimeDto>> SubmitPreApprovalAsync(
        OvertimePreApprovalRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OvertimeDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<OvertimeDto>.Failure(
                "No employee record is linked to the current user.", 403);

        if (employee.Status is EmployeeStatus.Terminated or EmployeeStatus.Inactive)
            return Result<OvertimeDto>.Failure(
                "Overtime pre-approval is not allowed for your current employment status.", 403);

        if (request.ExpectedHours <= 0)
            return Result<OvertimeDto>.Failure(
                "Expected overtime hours must be greater than zero.", 400, "invalid_hours");

        // No past dates for a pre-approval (the intent is to work overtime). "Today" is UTC, consistent
        // with the rest of the module (tenant-timezone deferred).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.Date < today)
            return Result<OvertimeDto>.Failure(
                "Overtime pre-approval cannot be submitted for a past date.", 400, "past_date");

        var expectedMinutes = (int)Math.Round(request.ExpectedHours * 60m, MidpointRounding.AwayFromZero);

        var settings = await GetOrCreateSettingsAsync(employee, cancellationToken);
        // BUG-286 location-scoped holiday + BUG-285 resolved work-week, same as the auto-detect path above —
        // a pre-approval must quote the SAME multiplier the detected overtime will later be paid at.
        var isHoliday = await IsPublicHolidayAsync(request.Date, employee.LocationId, cancellationToken);
        var workingDays = await ResolveWorkingDaysAsync(employee.Id, request.Date, cancellationToken);
        var (multiplier, multiplierBasis) = OvertimeMultiplierResolver.Resolve(
            request.Date, isHoliday,
            settings.WeekdayOvertimeMultiplier,
            settings.WeekendOvertimeMultiplier,
            settings.HolidayOvertimeMultiplier,
            workingDays);

        var record = new OvertimeRecord
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            AttendanceLogId = null,        // pre-approval precedes any clock-out (AC-2).
            Date = request.Date,
            OvertimeMinutes = expectedMinutes,
            ApprovedMinutes = null,
            Multiplier = multiplier,
            Type = OvertimeType.PreApproved,
            Status = OvertimeStatus.Pending,
            Reason = request.Reason.Trim(),
            IsPayrollReady = false,
            CalculationBasis = $"preApproval;expectedMinutes={expectedMinutes};" +
                               $"multiplier={multiplier};multiplierBasis={multiplierBasis}",
            WorkflowInstanceId = null,     // TODO(US-ADM-007): no workflow engine yet.
        };

        _dbContext.OvertimeRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // US-ADM-011c FR-11/AC-11: if the tenant has an Active Overtime approval workflow, instantiate it now so the
        // pre-approval routes through the configured steps. No active definition → WorkflowInstanceId stays null and
        // the legacy single-level manager approval governs it (AC-11). Only USER-submitted pre-approvals are wired —
        // the auto-detected clock-out path (BuildAutoDetectedAsync) is NOT a user submission and stays legacy.
        if (_workflowRuntime is not null)
        {
            var requestData = new Dictionary<string, object?>
            {
                ["overtime_minutes"] = record.OvertimeMinutes,
                ["multiplier"] = record.Multiplier,
                ["type"] = record.Type.ToString(),
            };
            var wf = await _workflowRuntime.CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Overtime, record.Id, employee.Id, requestData, cancellationToken);
            if (wf.InstanceCreated)
            {
                record.WorkflowInstanceId = wf.InstanceId;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // FR-8: notify the employee's manager that a pre-approval is pending (US-NTF-006 Phase 6). Never throws.
        if (_notifications is not null)
        {
            await _notifications.NotifyOvertimePreApprovalRequestedAsync(
                employee.Id, record.Date, record.OvertimeMinutes / 60m, cancellationToken);
        }

        _logger.LogInformation(
            "Employee {EmployeeId} submitted overtime pre-approval {Id} for {Date} (tenant {TenantId}).",
            employee.Id, record.Id, record.Date, _tenantContext.TenantId);

        return Result<OvertimeDto>.Success(ToDto(record));
    }

    // ══════════════════════════════════════════════════════════════
    //  My overtime (employee self-list)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<OvertimeDto>>> GetMyOvertimeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<OvertimeDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken, asNoTracking: true);
        if (employee is null)
            return Result<IReadOnlyList<OvertimeDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        var rows = await _dbContext.OvertimeRecords
            .AsNoTracking()
            .Where(o => o.EmployeeId == employee.Id)
            .OrderByDescending(o => o.Date)
            .ThenByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<OvertimeDto> dtos = rows.Select(ToDto).ToList();
        return Result<IReadOnlyList<OvertimeDto>>.Success(dtos);
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-3 / FR-5 — manager pending queue
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OvertimeQueueResult>> GetPendingForManagerAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OvertimeQueueResult>.Failure("Tenant context is not resolved.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken, asNoTracking: true);
        if (manager is null)
            return Result<OvertimeQueueResult>.Success(new OvertimeQueueResult { Items = [], TotalCount = 0 });

        // FR-5/AC-3: direct reports only (Phase 1 — mirrors US-ATT-004 direct-report scope).
        var teamIds = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == manager.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (teamIds.Count == 0)
            return Result<OvertimeQueueResult>.Success(new OvertimeQueueResult { Items = [], TotalCount = 0 });

        var teamSet = teamIds.ToHashSet();

        var query =
            from o in _dbContext.OvertimeRecords.AsNoTracking()
            join e in _dbContext.Employees.AsNoTracking() on o.EmployeeId equals e.Id
            where o.Status == OvertimeStatus.Pending && teamSet.Contains(o.EmployeeId)
            select new { o, e };

        var rows = await query
            .OrderBy(x => x.o.Date)
            .ThenBy(x => x.o.CreatedAt)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new OvertimeQueueItemDto
        {
            Id = x.o.Id,
            EmployeeId = x.o.EmployeeId,
            AttendanceLogId = x.o.AttendanceLogId,
            Date = x.o.Date,
            OvertimeMinutes = x.o.OvertimeMinutes,
            ApprovedMinutes = x.o.ApprovedMinutes,
            Multiplier = x.o.Multiplier,
            Type = x.o.Type,
            Status = x.o.Status,
            Reason = x.o.Reason,
            ManagerComment = x.o.ManagerComment,
            CreatedAt = x.o.CreatedAt,
            DailyCapApplied = x.o.DailyCapApplied,        // FR-8 / ISSUE-079
            WeeklyCapExceeded = x.o.WeeklyCapExceeded,     // FR-8 / ISSUE-079
            EmployeeName = $"{x.e.FirstName} {x.e.LastName}".Trim(),
            EmployeePhoto = x.e.ProfilePhotoUrl,
            SubmittedOn = x.o.CreatedAt,
        }).ToList();

        return Result<OvertimeQueueResult>.Success(
            new OvertimeQueueResult { Items = items, TotalCount = items.Count });
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-4 / FR-6 / FR-7 — approve (with adjust) / reject
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OvertimeDecisionDto>> ApproveAsync(
        Guid overtimeId, int? approvedMinutes, string? comment,
        CancellationToken cancellationToken = default)
    {
        // US-ADM-011c FR-11: a workflow-driven pre-approval routes through the runtime. The generic decision
        // endpoint carries no minute-adjust, so the workflow approve path uses the record's existing minutes; the
        // direct/legacy path below keeps the manager minute-adjust. Null → legacy path (AC-11).
        var routed = await TryWorkflowDecisionAsync(
            overtimeId, WorkflowDecisionAction.Approve, comment, cancellationToken);
        if (routed is not null)
            return routed;

        var ctx = await LoadForDecisionAsync(overtimeId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var record = ctx.Record!;
        var approverEmployeeId = ctx.ApproverEmployeeId;   // BUG-049: null for an employee-less HR approver.

        // FR-6/AC-4: approved minutes default to the recorded overtime; a manager adjustment must be
        // between 0 and the recorded overtime (cannot approve MORE than was worked).
        var finalApproved = approvedMinutes ?? record.OvertimeMinutes;
        if (finalApproved < 0 || finalApproved > record.OvertimeMinutes)
            return Result<OvertimeDecisionDto>.Failure(
                $"Approved minutes must be between 0 and the recorded overtime ({record.OvertimeMinutes}).",
                400, "invalid_approved_minutes");

        record.Status = OvertimeStatus.Approved;
        record.ApprovedMinutes = finalApproved;
        record.ManagerComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        record.IsPayrollReady = true;   // FR-7: approved overtime is payroll-ready.

        var history = NewHistory(
            record.Id, approverEmployeeId, OvertimeApprovalAction.Approved,
            finalApproved, record.ManagerComment, record.CalculationBasis);
        _dbContext.OvertimeApprovalHistories.Add(history);

        // NFR-3: single atomic SaveChanges (status + approved minutes + payroll flag + history).
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-8: notify the employee that their overtime was approved (US-NTF-006 Phase 6). Never throws.
        if (_notifications is not null)
        {
            await _notifications.NotifyOvertimeDecidedAsync(
                approved: true, record.EmployeeId, record.Date, finalApproved / 60m, reason: null, cancellationToken);
        }

        _logger.LogInformation(
            "Overtime {Id} APPROVED by approver {ApproverEmployeeId} (user {UserId}) for employee {EmployeeId}: " +
            "{ApprovedMinutes} min @ {Multiplier}x (tenant {TenantId}).",
            record.Id, approverEmployeeId, _currentUser.UserId, record.EmployeeId, finalApproved,
            record.Multiplier, _tenantContext.TenantId);

        return Result<OvertimeDecisionDto>.Success(new OvertimeDecisionDto
        {
            Id = record.Id,
            Status = record.Status,
            ApprovedMinutes = record.ApprovedMinutes,
            Multiplier = record.Multiplier,
            ManagerComment = record.ManagerComment,
            ActionedAt = history.ActionedAt,
        });
    }

    public async Task<Result<OvertimeDecisionDto>> RejectAsync(
        Guid overtimeId, string reason, CancellationToken cancellationToken = default)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length < 10)
            return Result<OvertimeDecisionDto>.Failure(
                "A rejection reason of at least 10 characters is required.", 400);

        // US-ADM-011c FR-11: route a workflow-driven pre-approval's rejection through the runtime (see ApproveAsync).
        var routed = await TryWorkflowDecisionAsync(
            overtimeId, WorkflowDecisionAction.Reject, trimmed, cancellationToken);
        if (routed is not null)
            return routed;

        var ctx = await LoadForDecisionAsync(overtimeId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var record = ctx.Record!;
        var approverEmployeeId = ctx.ApproverEmployeeId;   // BUG-049: null for an employee-less HR approver.

        record.Status = OvertimeStatus.Rejected;
        record.ApprovedMinutes = null;
        record.ManagerComment = trimmed;
        record.IsPayrollReady = false;

        var history = NewHistory(
            record.Id, approverEmployeeId, OvertimeApprovalAction.Rejected,
            null, trimmed, record.CalculationBasis);
        _dbContext.OvertimeApprovalHistories.Add(history);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-8: notify the employee that their overtime was rejected, with the reason (US-NTF-006 Phase 6). Never throws.
        if (_notifications is not null)
        {
            await _notifications.NotifyOvertimeDecidedAsync(
                approved: false, record.EmployeeId, record.Date, record.OvertimeMinutes / 60m, trimmed, cancellationToken);
        }

        _logger.LogInformation(
            "Overtime {Id} REJECTED by approver {ApproverEmployeeId} (user {UserId}) for employee {EmployeeId} (tenant {TenantId}).",
            record.Id, approverEmployeeId, _currentUser.UserId, record.EmployeeId, _tenantContext.TenantId);

        return Result<OvertimeDecisionDto>.Success(new OvertimeDecisionDto
        {
            Id = record.Id,
            Status = record.Status,
            ApprovedMinutes = null,
            Multiplier = record.Multiplier,
            ManagerComment = trimmed,
            ActionedAt = history.ActionedAt,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Workflow-driven approval routing (US-ADM-011c FR-11)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// US-ADM-011c FR-11: routes an approve/reject decision through the generic workflow runtime when the overtime
    /// record is workflow-driven (its <see cref="OvertimeRecord.WorkflowInstanceId"/> is set). Returns <c>null</c>
    /// when it is NOT workflow-driven (or the runtime is unavailable), so the caller runs the legacy single-level
    /// path unchanged (AC-11). The status/payroll side-effects run inside the runtime's transaction via the
    /// callbacks so the workflow and overtime state commit atomically.
    /// </summary>
    private async Task<Result<OvertimeDecisionDto>?> TryWorkflowDecisionAsync(
        Guid overtimeId, WorkflowDecisionAction action, string? comment, CancellationToken cancellationToken)
    {
        if (_workflowRuntime is null || !_tenantContext.IsResolved)
            return null;

        var peek = await _dbContext.OvertimeRecords.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == overtimeId, cancellationToken);
        if (peek is null || peek.WorkflowInstanceId is null)
            return null; // legacy single-level path (AC-11)

        var decision = await _workflowRuntime.DecideAsync(
            WorkflowEntityType.Overtime, overtimeId, action, comment,
            onApproved: ct => StageOtApprovalAsync(overtimeId, comment, ct),
            onRejected: ct => StageOtRejectionAsync(overtimeId, comment, ct),
            cancellationToken: cancellationToken);

        if (decision.IsFailure)
            return Result<OvertimeDecisionDto>.Failure(
                decision.Error!, decision.StatusCode ?? 400, decision.ErrorCode);

        var outcome = decision.Value!;
        var now = DateTime.UtcNow;

        switch (outcome.Outcome)
        {
            case WorkflowDecisionOutcome.StepAdvanced:
            case WorkflowDecisionOutcome.StepRecorded:
                // Intermediate approval — the record stays Pending until the final step approves.
                return Result<OvertimeDecisionDto>.Success(new OvertimeDecisionDto
                {
                    Id = overtimeId,
                    Status = OvertimeStatus.Pending,
                    ApprovedMinutes = null,
                    Multiplier = peek.Multiplier,
                    ManagerComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
                    ActionedAt = now,
                });

            case WorkflowDecisionOutcome.InstanceApproved:
                return Result<OvertimeDecisionDto>.Success(new OvertimeDecisionDto
                {
                    Id = overtimeId,
                    Status = OvertimeStatus.Approved,
                    ApprovedMinutes = peek.OvertimeMinutes,
                    Multiplier = peek.Multiplier,
                    ManagerComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
                    ActionedAt = now,
                });

            case WorkflowDecisionOutcome.InstanceRejected:
            default:
                return Result<OvertimeDecisionDto>.Success(new OvertimeDecisionDto
                {
                    Id = overtimeId,
                    Status = OvertimeStatus.Rejected,
                    ApprovedMinutes = null,
                    Multiplier = peek.Multiplier,
                    ManagerComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
                    ActionedAt = now,
                });
        }
    }

    /// <summary>
    /// Stages the overtime-approve side-effect (status → APPROVED + approved-minutes + payroll-ready flag +
    /// history) WITHOUT saving — the runtime owns the commit. The generic decision path carries no minute-adjust,
    /// so it approves the record's existing overtime minutes (the legacy path keeps the manager adjust).
    /// </summary>
    private async Task<Result> StageOtApprovalAsync(
        Guid overtimeId, string? comment, CancellationToken cancellationToken)
    {
        var record = await _dbContext.OvertimeRecords
            .FirstOrDefaultAsync(o => o.Id == overtimeId, cancellationToken);
        if (record is null)
            return Result.Failure("Overtime record not found.", 404);

        var approver = await GetCurrentEmployeeAsync(cancellationToken);

        record.Status = OvertimeStatus.Approved;
        record.ApprovedMinutes = record.OvertimeMinutes;
        record.ManagerComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        record.IsPayrollReady = true; // FR-7: approved overtime is payroll-ready.

        _dbContext.OvertimeApprovalHistories.Add(NewHistory(
            record.Id, approver?.Id, OvertimeApprovalAction.Approved,
            record.ApprovedMinutes, record.ManagerComment, record.CalculationBasis));

        // NO SaveChanges — the runtime commits this atomically with the workflow transition.
        return Result.Success();
    }

    /// <summary>Stages the overtime-reject side-effect (status → REJECTED + history) WITHOUT saving.</summary>
    private async Task StageOtRejectionAsync(
        Guid overtimeId, string? reason, CancellationToken cancellationToken)
    {
        var record = await _dbContext.OvertimeRecords
            .FirstOrDefaultAsync(o => o.Id == overtimeId, cancellationToken);
        if (record is null)
            return;

        var approver = await GetCurrentEmployeeAsync(cancellationToken);

        record.Status = OvertimeStatus.Rejected;
        record.ApprovedMinutes = null;
        record.ManagerComment = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        record.IsPayrollReady = false;

        _dbContext.OvertimeApprovalHistories.Add(NewHistory(
            record.Id, approver?.Id, OvertimeApprovalAction.Rejected,
            null, record.ManagerComment, record.CalculationBasis));
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-5 — monthly HR report
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OvertimeReportResult>> GetMonthlyReportAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OvertimeReportResult>.Failure("Tenant context is not resolved.", 400);

        if (month is < 1 or > 12)
            return Result<OvertimeReportResult>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var query =
            from o in _dbContext.OvertimeRecords.AsNoTracking()
            join e in _dbContext.Employees.AsNoTracking() on o.EmployeeId equals e.Id
            where o.Date >= monthStart && o.Date <= monthEnd
            select new { o.EmployeeId, EmployeeName = e.FirstName + " " + e.LastName, o.Status, o.OvertimeMinutes, o.ApprovedMinutes };

        var rows = await query.ToListAsync(cancellationToken);

        var items = rows
            .GroupBy(r => new { r.EmployeeId, r.EmployeeName })
            .Select(g => new OvertimeReportRowDto
            {
                EmployeeId = g.Key.EmployeeId,
                EmployeeName = (g.Key.EmployeeName ?? string.Empty).Trim(),
                ApprovedMinutes = g.Where(x => x.Status == OvertimeStatus.Approved)
                                   .Sum(x => x.ApprovedMinutes ?? 0),
                PendingMinutes = g.Where(x => x.Status == OvertimeStatus.Pending)
                                  .Sum(x => x.OvertimeMinutes),
                RejectedMinutes = g.Where(x => x.Status == OvertimeStatus.Rejected)
                                   .Sum(x => x.OvertimeMinutes),
                // BR-6 / ISSUE-080: surface UNAPPROVED minutes (pre-approval required, none matched) that
                // were counted in RecordCount but previously omitted from every minute column.
                UnapprovedMinutes = g.Where(x => x.Status == OvertimeStatus.Unapproved)
                                     .Sum(x => x.OvertimeMinutes),
                RecordCount = g.Count(),
            })
            .OrderBy(r => r.EmployeeName)
            .ToList();

        var totals = new OvertimeReportTotals
        {
            ApprovedMinutes = items.Sum(i => i.ApprovedMinutes),
            PendingMinutes = items.Sum(i => i.PendingMinutes),
            RejectedMinutes = items.Sum(i => i.RejectedMinutes),
            UnapprovedMinutes = items.Sum(i => i.UnapprovedMinutes),
            RecordCount = items.Sum(i => i.RecordCount),
        };

        return Result<OvertimeReportResult>.Success(new OvertimeReportResult
        {
            Month = $"{year:D4}-{month:D2}",
            Items = items,
            Totals = totals,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Shared load + authorization (BR-8 self-approval, direct-report, immutability)
    // ══════════════════════════════════════════════════════════════

    private async Task<DecisionContext> LoadForDecisionAsync(
        Guid overtimeId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return DecisionContext.Fail("Tenant context is not resolved.", 400);

        // BUG-049: authorization is PERMISSION-driven, not employee-record-driven. An approver holding
        // Attendance.Approve.Team (the permission the controller already gates on — granted to managers AND
        // to the HR roles) may reach the decision even with NO linked employee record (an HR persona). The
        // approver's own employee record, when present, still drives the manager-of-record path plus the
        // self-approval (BR-8) and direct-report (FR-5/AC-3) checks below.
        var hasApprovePermission = _currentUser.Permissions.Contains(PermissionCatalog.Attendance.ApproveTeam);
        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null && !hasApprovePermission)
            return DecisionContext.Fail("No employee record is linked to the current user.", 403);

        var record = await _dbContext.OvertimeRecords
            .FirstOrDefaultAsync(o => o.Id == overtimeId, cancellationToken);
        if (record is null)
            return DecisionContext.Fail("Overtime record not found.", 404);

        // BR-8: an approver cannot approve their OWN overtime — checked before the team check. Only
        // relevant when the approver has an employee record (an employee-less HR actor has no "own" record).
        if (manager is not null && record.EmployeeId == manager.Id)
            return DecisionContext.Fail(
                "You cannot approve your own overtime. It must be approved by your manager or HR.",
                403, "self_approval");

        // FR-5/AC-3: direct reports only for the manager-of-record path. BUG-049: an all-scope HR approver
        // (Attendance.View.All) OR an approver with no reporting line (no employee record but holding the
        // approve permission — the HR persona) may approve beyond their direct reports; a plain manager
        // (an employee record + the approve permission but no all-scope) stays limited to direct reports.
        var requester = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == record.EmployeeId, cancellationToken);
        if (requester is null)
            return DecisionContext.Fail(
                "You are not authorized to approve overtime for this employee.", 403, "not_team_member");

        var isDirectManager = manager is not null && requester.ReportsToEmployeeId == manager.Id;
        var hasAllScope = _currentUser.Permissions.Contains(PermissionCatalog.Attendance.ViewAll);
        var isPermissionOnlyApprover = manager is null && hasApprovePermission;
        if (!isDirectManager && !hasAllScope && !isPermissionOnlyApprover)
            return DecisionContext.Fail(
                "You are not authorized to approve overtime for this employee.", 403, "not_team_member");

        // Immutability: only PENDING records can be actioned (APPROVED/REJECTED/UNAPPROVED are terminal
        // for the manager — UNAPPROVED is routed to HR, not the manager).
        if (record.Status != OvertimeStatus.Pending)
            return DecisionContext.Fail(
                $"This overtime record has already been actioned (status: {record.Status}).",
                409, "already_actioned");

        return new DecisionContext { Record = record, ApproverEmployeeId = manager?.Id };
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private Task<Employee?> GetCurrentEmployeeAsync(
        CancellationToken cancellationToken, bool asNoTracking = false)
    {
        var q = _dbContext.Employees.AsQueryable();
        if (asNoTracking) q = q.AsNoTracking();
        return q.FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
    }

    /// <summary>
    /// Resolves the employee's standard work minutes for a date. ISSUE-078: the tenant
    /// <see cref="AttendanceSettings.StandardWorkMinutes"/> is the CANONICAL standard workday and is used
    /// whenever configured (&gt; 0), so auto-detected overtime uses the SAME baseline as the monthly report
    /// / summary (US-ATT-006 AC-5 / US-ATT-007 / US-ATT-008 define the standard as a tenant setting) and the
    /// two reconcile. The employee's assigned shift span (US-ATT-005, start/end minus break; FLEXIBLE uses
    /// MinimumHours) is only a FALLBACK for tenants that have not configured a standard.
    /// </summary>
    private async Task<int> ResolveStandardMinutesAsync(
        Guid employeeId, DateOnly date, AttendanceSettings settings, CancellationToken cancellationToken)
    {
        // ISSUE-078: canonical — the tenant standard, matching the report/summary. Falls through to the
        // shift-derived span only when no tenant standard is configured.
        if (settings.StandardWorkMinutes > 0)
            return settings.StandardWorkMinutes;

        var assignment = await _dbContext.EmployeeShifts
            .AsNoTracking()
            .Where(es => es.EmployeeId == employeeId
                && es.EffectiveFrom <= date
                && (es.EffectiveTo == null || es.EffectiveTo >= date))
            .OrderByDescending(es => es.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        Shift? shift = null;
        if (assignment is not null)
        {
            shift = await _dbContext.Shifts
                .AsNoTracking()
                .Include(s => s.RotationSteps)
                .FirstOrDefaultAsync(s => s.Id == assignment.ShiftId, cancellationToken);
        }

        shift ??= await _dbContext.Shifts
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsDefault, cancellationToken);

        if (shift is null)
            return settings.StandardWorkMinutes;   // AC-1 fallback: no shift -> tenant setting.

        var standard = ShiftStandardMinutes(shift);
        return standard ?? settings.StandardWorkMinutes;
    }

    /// <summary>
    /// Standard work minutes a shift represents: (end - start) less break for SINGLE/ROTATING headers
    /// (handles overnight where end &lt; start), or MinimumHours*60 for FLEXIBLE. Null when undeterminable
    /// (caller falls back to the tenant setting).
    /// </summary>
    private static int? ShiftStandardMinutes(Shift shift)
    {
        if (shift.Type == ShiftType.Flexible)
            return shift.MinimumHours.HasValue
                ? (int)Math.Round(shift.MinimumHours.Value * 60m, MidpointRounding.AwayFromZero)
                : (int?)null;

        if (shift.StartTime is null || shift.EndTime is null)
            return null;

        var startMin = shift.StartTime.Value.ToTimeSpan().TotalMinutes;
        var endMin = shift.EndTime.Value.ToTimeSpan().TotalMinutes;
        var span = endMin - startMin;
        if (span <= 0) span += 24 * 60;   // overnight shift (§10 night shift allowed).

        var net = (int)Math.Round(span, MidpointRounding.AwayFromZero) - shift.BreakDurationMinutes;
        return net > 0 ? net : (int?)null;
    }

    /// <summary>
    /// BR-3/BR-7: whether the date is an active public holiday for THIS employee (US-LV-007 BR-2 scoping).
    ///
    /// <para>BUG-286: this used to run its own inline query matching only
    /// <c>Date == date &amp;&amp; IsActive &amp;&amp; Type == Public</c> with **no LocationId filter**, bypassing the
    /// location-aware <see cref="IHolidayProvider"/> that leave already used. A New-York-only holiday
    /// therefore granted a London employee the holiday OT multiplier (and vice-versa) — the wrong rate
    /// flowing straight into payroll earnings. Holiday lookups were duplicated in three places; this routes
    /// overtime onto the same provider as leave so there is one location-aware source of truth.</para>
    /// </summary>
    private async Task<bool> IsPublicHolidayAsync(
        DateOnly date, Guid? locationId, CancellationToken cancellationToken)
    {
        if (_holidayProvider is not null)
        {
            var holidays = await _holidayProvider.GetHolidaysAsync(date, date, locationId, cancellationToken);
            return holidays.Contains(date);
        }

        // Legacy fallback for unit fixtures that construct this service without the provider. DI always
        // supplies it, so production never takes this branch — but note it is NOT location-scoped, so any
        // test relying on it cannot prove BUG-286.
        return await _dbContext.Holidays
            .AsNoTracking()
            .AnyAsync(h => h.Date == date && h.IsActive && h.Type == HolidayType.Public, cancellationToken);
    }

    /// <summary>
    /// BUG-285 / US-ATT-011 AC-2: the employee's resolved working-weekday set (ISO 1=Mon..7=Sun) as of
    /// <paramref name="date"/>, via the shared four-tier chain — the SAME working-week source attendance,
    /// leave and payroll use. Passed to <see cref="OvertimeMultiplierResolver"/> so "weekend" means "not a
    /// working day for THIS employee" rather than a hardcoded Sat/Sun.
    /// </summary>
    private async Task<IReadOnlySet<int>> ResolveWorkingDaysAsync(
        Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            _dbContext, [employeeId], date, cancellationToken);

        return sets.TryGetValue(employeeId, out var days) ? days : new HashSet<int>();
    }

    /// <summary>
    /// BR-5/FR-8: returns true if adding <paramref name="newMinutes"/> pushes the employee's
    /// approved+pending overtime in the Monday-anchored week of <paramref name="date"/> over the max.
    /// </summary>
    private async Task<bool> EvaluateWeeklyCapAsync(
        Guid employeeId, DateOnly date, int newMinutes, int maxWeeklyMinutes, CancellationToken cancellationToken)
    {
        if (maxWeeklyMinutes <= 0)
            return false;

        // Monday-anchored ISO week containing the date.
        var dow = (int)date.DayOfWeek;            // Sun=0..Sat=6
        var daysSinceMonday = (dow + 6) % 7;      // Mon=0
        var weekStart = date.AddDays(-daysSinceMonday);
        var weekEnd = weekStart.AddDays(6);

        var existing = await _dbContext.OvertimeRecords
            .AsNoTracking()
            .Where(o => o.EmployeeId == employeeId
                     && o.Date >= weekStart && o.Date <= weekEnd
                     && (o.Status == OvertimeStatus.Approved || o.Status == OvertimeStatus.Pending))
            .Select(o => o.Status == OvertimeStatus.Approved ? (o.ApprovedMinutes ?? 0) : o.OvertimeMinutes)
            .ToListAsync(cancellationToken);

        return existing.Sum() + newMinutes > maxWeeklyMinutes;
    }

    /// <summary>
    /// US-ATT-011 AC-3 (CAL-4): the EFFECTIVE policy for this employee — their Location's override, else the
    /// tenant default, else a lazily-created tenant-default row.
    ///
    /// <para>⚠ Was an unpredicated <c>FirstOrDefaultAsync()</c>, safe only while a unique index on TenantId
    /// alone guaranteed one settings row per tenant. Location overrides break that invariant: an
    /// unpredicated read would return an ARBITRARY row, so one branch's OT multipliers/caps could be applied
    /// to every employee in the tenant. This is a money path — resolve per employee.</para>
    /// </summary>
    private Task<AttendanceSettings> GetOrCreateSettingsAsync(
        Employee employee, CancellationToken cancellationToken)
        => AttendancePolicyResolver.ResolveForLocationAsync(
            _dbContext, employee.LocationId, cancellationToken);

    private OvertimeApprovalHistory NewHistory(
        Guid overtimeRecordId, Guid? approverEmployeeId, string action,
        int? approvedMinutes, string? comment, string? calculationBasis) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        OvertimeRecordId = overtimeRecordId,
        ApproverEmployeeId = approverEmployeeId,
        ApprovalLevel = 1,
        Action = action,
        ApprovedMinutes = approvedMinutes,
        Comment = comment,
        CalculationBasis = calculationBasis,
        ActionedAt = DateTime.UtcNow,
    };

    private static OvertimeDto ToDto(OvertimeRecord o) => new()
    {
        Id = o.Id,
        EmployeeId = o.EmployeeId,
        AttendanceLogId = o.AttendanceLogId,
        Date = o.Date,
        OvertimeMinutes = o.OvertimeMinutes,
        ApprovedMinutes = o.ApprovedMinutes,
        Multiplier = o.Multiplier,
        Type = o.Type,
        Status = o.Status,
        Reason = o.Reason,
        ManagerComment = o.ManagerComment,
        CreatedAt = o.CreatedAt,
        DailyCapApplied = o.DailyCapApplied,        // FR-8 / ISSUE-079
        WeeklyCapExceeded = o.WeeklyCapExceeded,     // FR-8 / ISSUE-079
    };

    private sealed class DecisionContext
    {
        public OvertimeRecord? Record { get; init; }

        /// <summary>
        /// BUG-049: the approver's employee id, or null when the approver is a permission-holder with no
        /// linked employee record (an HR persona). Written to the immutable decision history.
        /// </summary>
        public Guid? ApproverEmployeeId { get; init; }

        public Result<OvertimeDecisionDto>? Failure { get; init; }

        public static DecisionContext Fail(string error, int statusCode, string? errorCode = null) => new()
        {
            Failure = Result<OvertimeDecisionDto>.Failure(error, statusCode, errorCode),
        };
    }
}
