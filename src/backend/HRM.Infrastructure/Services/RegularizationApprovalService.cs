using System.Text.Json;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Manager approve/reject of attendance regularization requests (US-ATT-004). All queries are
/// tenant-scoped via ITenantContext + EF global query filters (NFR-3: this codebase uses EF global
/// filters + TenantInterceptor, NOT PostgreSQL RLS). Mirrors the established single-level
/// direct-report approval pattern used by LeaveRequestService (US-LV-005).
///
/// Deferred / flagged dependencies (do NOT block — owned by their stories):
///   - Approval Workflow Engine (FR-4/AC-4 multi-level): NONE exists (US-ADM-007). This implements
///     SINGLE-LEVEL approval — the manager's approve is the FINAL approve and immediately mutates the
///     attendance_log. workflow_instance_id stays null. AC-4 (status stays Pending until all levels
///     approve) CANNOT be satisfied without the engine; documented, not faked.
///   - Redis dashboard cache (FR-8): no Redis in this codebase — skipped (same as US-ATT-001/002/003).
///
/// Notifications (FR-5): the employee is notified of the approve/reject decision via
/// IAttendanceNotificationService (US-NTF-006 Phase 6) — real in-app + email delivery through
/// the notification dispatcher (best-effort; a delivery failure never blocks the committed decision).
/// </summary>
public sealed class RegularizationApprovalService : IRegularizationApprovalService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IShiftService _shiftService;
    private readonly IWorkflowRuntime? _workflowRuntime;
    private readonly IAttendanceNotificationService? _notifications;
    private readonly ILogger<RegularizationApprovalService> _logger;

    public RegularizationApprovalService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IShiftService shiftService,
        ILogger<RegularizationApprovalService> logger,
        IWorkflowRuntime? workflowRuntime = null,
        IAttendanceNotificationService? notifications = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _shiftService = shiftService;
        // US-ADM-011c FR-11: optional so existing US-ATT-004 unit tests keep compiling; DI always supplies it.
        // When the regularization is workflow-driven (WorkflowInstanceId set), decisions route through the runtime.
        _workflowRuntime = workflowRuntime;
        // US-NTF-006 Phase 6: optional so existing US-ATT-004 unit tests keep compiling; DI always supplies the Real impl.
        _notifications = notifications;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Pending queue (FR-1 / AC-3 / NFR-1)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PendingRegularizationQueueResult>> GetPendingForManagerAsync(
        PendingRegularizationQueryParams queryParams,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PendingRegularizationQueueResult>.Failure("Tenant context is not resolved.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
        {
            // No employee record linked to the user -> empty queue (not an error), mirroring US-LV-004.
            return Result<PendingRegularizationQueueResult>.Success(
                new PendingRegularizationQueueResult { Items = [], TotalCount = 0 });
        }

        // FR-7 / AC-5: direct reports only (Phase 1 — see the direct-vs-hierarchy note in the vault).
        // Both Employee and AttendanceRegularization are tenant-scoped by the global query filter.
        var teamMemberIds = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == manager.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (teamMemberIds.Count == 0)
            return Result<PendingRegularizationQueueResult>.Success(
                new PendingRegularizationQueueResult { Items = [], TotalCount = 0 });

        var teamIdSet = teamMemberIds.ToHashSet();

        // NFR-1: project straight to the DTO via a join (no N+1, no entity tracking).
        var query =
            from r in _dbContext.AttendanceRegularizations.AsNoTracking()
            join e in _dbContext.Employees.AsNoTracking() on r.EmployeeId equals e.Id
            where r.Status == RegularizationStatus.Pending && teamIdSet.Contains(r.EmployeeId)
            select new { r, e };

        if (queryParams.EmployeeId.HasValue)
            query = query.Where(x => x.r.EmployeeId == queryParams.EmployeeId.Value);
        if (queryParams.FromDate.HasValue)
            query = query.Where(x => x.r.Date >= queryParams.FromDate.Value);
        if (queryParams.ToDate.HasValue)
            query = query.Where(x => x.r.Date <= queryParams.ToDate.Value);

        var rows = await query
            .OrderBy(x => x.r.Date)            // AC-3: oldest pending first.
            .ThenBy(x => x.r.CreatedAt)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new PendingRegularizationDto
        {
            RegularizationId = x.r.Id,
            EmployeeId = x.r.EmployeeId,
            EmployeeName = $"{x.e.FirstName} {x.e.LastName}".Trim(),
            EmployeePhoto = x.e.ProfilePhotoUrl,
            Date = x.r.Date,
            RegularizationType = x.r.RegularizationType,
            RequestedClockIn = x.r.RequestedClockIn,
            RequestedClockOut = x.r.RequestedClockOut,
            Reason = x.r.Reason,
            SubmittedOn = x.r.CreatedAt,
        }).ToList();

        return Result<PendingRegularizationQueueResult>.Success(
            new PendingRegularizationQueueResult { Items = items, TotalCount = items.Count });
    }

    // ══════════════════════════════════════════════════════════════
    //  Approve (AC-1 / FR-2, BR-3/BR-5/BR-6) — final single-level approve
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<RegularizationDecisionDto>> ApproveAsync(
        Guid regularizationId,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        // US-ADM-011c FR-11: when this regularization is workflow-driven, route the decision through the generic
        // runtime (approver authz + multi-level advance + single-winner transaction). Null → legacy path (AC-11).
        var routed = await TryWorkflowDecisionAsync(
            regularizationId, WorkflowDecisionAction.Approve, comment, cancellationToken);
        if (routed is not null)
            return routed;

        var ctx = await LoadForDecisionAsync(regularizationId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        return await ApproveCoreAsync(ctx.Regularization!, ctx.Manager!, comment, cancellationToken);
    }

    // ══════════════════════════════════════════════════════════════
    //  Reject (AC-2 / FR-3, BR-1/BR-3/BR-6)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<RegularizationDecisionDto>> RejectAsync(
        Guid regularizationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        // BR-1: reason is mandatory, min 10 chars (also enforced by the validator; guard defensively).
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length < 10)
            return Result<RegularizationDecisionDto>.Failure(
                "A rejection reason of at least 10 characters is required.", 400);

        // US-ADM-011c FR-11: route a workflow-driven regularization's rejection through the runtime (see ApproveAsync).
        var routed = await TryWorkflowDecisionAsync(
            regularizationId, WorkflowDecisionAction.Reject, trimmed, cancellationToken);
        if (routed is not null)
            return routed;

        var ctx = await LoadForDecisionAsync(regularizationId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var regularization = ctx.Regularization!;
        var manager = ctx.Manager!;

        // ISSUE-073: snapshot the pending state before the decision so the audit captures before/after.
        var beforeSnapshot = SnapshotRegularization(regularization);

        // AC-2 / FR-3: status -> REJECTED, store the reason. The attendance_log is NOT touched.
        regularization.Status = RegularizationStatus.Rejected;

        var history = NewHistory(regularization.Id, manager.Id, RegularizationApprovalAction.Rejected, trimmed);
        _dbContext.RegularizationApprovalHistories.Add(history);

        // ISSUE-073: queryable tenant audit row (before/after) for the reject, same transaction.
        AddRegularizationAudit("AttendanceRegularization.Rejected", regularization.Id,
            before: beforeSnapshot, after: SnapshotRegularization(regularization),
            detail: $"Rejected by manager {manager.Id}. Reason: {trimmed}");

        // NFR-2: single atomic SaveChanges (status + immutable history together).
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-5: notify the employee that their request was rejected, with the reason (US-NTF-006 Phase 6). Never throws.
        // FR-8 (Redis cache update): skipped — no Redis in this codebase.
        if (_notifications is not null)
        {
            await _notifications.NotifyRegularizationDecidedAsync(
                approved: false, regularization.EmployeeId, regularization.Date, trimmed, cancellationToken);
        }

        _logger.LogInformation(
            "Regularization {RegId} REJECTED by manager {ManagerEmployeeId} for employee {EmployeeId} " +
            "in tenant {TenantId}. Action {AuditAction} resource {Resource}.",
            regularization.Id, manager.Id, regularization.EmployeeId, _tenantContext.TenantId,
            "Attendance.Regularization.Rejected", "AttendanceRegularization");

        return Result<RegularizationDecisionDto>.Success(new RegularizationDecisionDto
        {
            RegularizationId = regularization.Id,
            Status = regularization.Status,
            Action = RegularizationApprovalAction.Rejected,
            ApprovalLevel = history.ApprovalLevel,
            Comment = trimmed,
            ActionedAt = history.ActionedAt,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Bulk approve (BR-7)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<BulkApproveRegularizationResult>> BulkApproveAsync(
        IReadOnlyList<Guid> regularizationIds,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BulkApproveRegularizationResult>.Failure("Tenant context is not resolved.", 400);

        if (regularizationIds is null || regularizationIds.Count == 0)
            return Result<BulkApproveRegularizationResult>.Failure(
                "At least one regularization id is required.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return Result<BulkApproveRegularizationResult>.Failure(
                "No employee record is linked to the current user.", 403);

        var items = new List<BulkRegularizationItemResult>(regularizationIds.Count);

        // BR-7: each item runs the full per-item authorization + lock pipeline independently. Each
        // successful approve commits in its own SaveChanges so a blocked item never reverts an
        // already-approved one. De-duplicate ids to avoid double-processing.
        foreach (var id in regularizationIds.Distinct())
        {
            // US-ADM-011c FR-11: a workflow-driven item routes through the runtime (each commits per item via the
            // runtime). Non-workflow items fall through to the legacy per-item approve below.
            var routed = await TryWorkflowDecisionAsync(id, WorkflowDecisionAction.Approve, comment, cancellationToken);
            if (routed is not null)
            {
                items.Add(new BulkRegularizationItemResult
                {
                    RegularizationId = id,
                    Succeeded = routed.IsSuccess,
                    Decision = routed.IsSuccess ? routed.Value : null,
                    Error = routed.IsFailure ? routed.Error : null,
                    ErrorCode = routed.IsFailure ? routed.ErrorCode : null,
                });
                continue;
            }

            var ctx = await LoadForDecisionAsync(id, cancellationToken);
            if (ctx.Failure is not null)
            {
                items.Add(new BulkRegularizationItemResult
                {
                    RegularizationId = id,
                    Succeeded = false,
                    Error = ctx.Failure.Error,
                    ErrorCode = ctx.Failure.ErrorCode,
                });
                continue;
            }

            var result = await ApproveCoreAsync(ctx.Regularization!, ctx.Manager!, comment, cancellationToken);
            items.Add(new BulkRegularizationItemResult
            {
                RegularizationId = id,
                Succeeded = result.IsSuccess,
                Decision = result.IsSuccess ? result.Value : null,
                Error = result.IsFailure ? result.Error : null,
                ErrorCode = result.IsFailure ? result.ErrorCode : null,
            });
        }

        int succeeded = items.Count(i => i.Succeeded);
        return Result<BulkApproveRegularizationResult>.Success(new BulkApproveRegularizationResult
        {
            TotalRequested = items.Count,
            SucceededCount = succeeded,
            FailedCount = items.Count - succeeded,
            Items = items,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Core approve (shared by single + bulk)
    // ══════════════════════════════════════════════════════════════

    private async Task<Result<RegularizationDecisionDto>> ApproveCoreAsync(
        AttendanceRegularization regularization,
        Employee manager,
        string? comment,
        CancellationToken cancellationToken)
    {
        // BR-5: re-check the period lock AT APPROVAL TIME (a period may have locked since submission).
        // US-ATT-009: the canonical AttendancePeriodLock — only ACTIVE (IsLocked) rows freeze dates.
        var locked = await _dbContext.AttendancePeriodLocks
            .AnyAsync(p => p.IsLocked && p.PeriodStart <= regularization.Date && p.PeriodEnd >= regularization.Date,
                cancellationToken);
        if (locked)
            return Result<RegularizationDecisionDto>.Failure(
                "This date falls within a locked payroll period. Please contact HR.",
                409, "payroll_period_locked");

        // FR-2 / AC-1: create or update the attendance_log for the employee + regularized day, then
        // recalculate work hours via the SAME AttendanceCalculator clock-out uses.
        var settings = await GetOrCreateSettingsAsync(cancellationToken);

        var logResult = await ApplyToAttendanceLogAsync(regularization, settings, cancellationToken);
        if (logResult.IsFailure)
            return Result<RegularizationDecisionDto>.Failure(
                logResult.Error!, logResult.StatusCode ?? 400, logResult.ErrorCode);

        var log = logResult.Value!;

        // ISSUE-073: snapshot the pending state before the decision so the audit captures before/after.
        var beforeSnapshot = SnapshotRegularization(regularization);

        // AC-1: final single-level approve. workflow_instance_id stays null (no engine — US-ADM-007).
        regularization.Status = RegularizationStatus.Approved;
        regularization.AttendanceLogId = log.Id;

        var history = NewHistory(
            regularization.Id, manager.Id, RegularizationApprovalAction.Approved,
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
        _dbContext.RegularizationApprovalHistories.Add(history);

        // ISSUE-073: queryable tenant audit row (before/after) for the approve, same transaction.
        AddRegularizationAudit("AttendanceRegularization.Approved", regularization.Id,
            before: beforeSnapshot, after: SnapshotRegularization(regularization),
            detail: $"Approved by manager {manager.Id} → attendance_log {log.Id} "
                  + $"({log.TotalWorkMinutes} min, status {log.Status})."
                  + (string.IsNullOrWhiteSpace(comment) ? string.Empty : $" Comment: {comment.Trim()}"));

        // NFR-2: a SINGLE atomic SaveChanges commits the regularization status, the linked-log id, the
        // attendance_log create/update, AND the immutable history row together — or none of them.
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-5: notify the employee that their request was approved (US-NTF-006 Phase 6). Never throws.
        // FR-8 (Redis daily-status cache): skipped — no Redis in this codebase.
        if (_notifications is not null)
        {
            await _notifications.NotifyRegularizationDecidedAsync(
                approved: true, regularization.EmployeeId, regularization.Date, regularization.Reason, cancellationToken);
        }

        _logger.LogInformation(
            "Regularization {RegId} APPROVED by manager {ManagerEmployeeId} for employee {EmployeeId}: " +
            "attendance_log {LogId} ({TotalMinutes} min, status {Status}) in tenant {TenantId}. " +
            "Action {AuditAction} resource {Resource}.",
            regularization.Id, manager.Id, regularization.EmployeeId, log.Id,
            log.TotalWorkMinutes, log.Status, _tenantContext.TenantId,
            "Attendance.Regularization.Approved", "AttendanceRegularization");

        return Result<RegularizationDecisionDto>.Success(new RegularizationDecisionDto
        {
            RegularizationId = regularization.Id,
            Status = regularization.Status,
            Action = RegularizationApprovalAction.Approved,
            ApprovalLevel = history.ApprovalLevel,
            AttendanceLogId = log.Id,
            TotalWorkMinutes = log.TotalWorkMinutes,
            OvertimeMinutes = log.OvertimeMinutes,
            AttendanceStatus = log.Status,
            Comment = history.Comment,
            ActionedAt = history.ActionedAt,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Workflow-driven approval routing (US-ADM-011c FR-11)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// US-ADM-011c FR-11: routes an approve/reject decision through the generic workflow runtime when the
    /// regularization is workflow-driven (its <see cref="AttendanceRegularization.WorkflowInstanceId"/> is set).
    /// Returns <c>null</c> when it is NOT workflow-driven (or the runtime is unavailable), so the caller runs the
    /// legacy single-level path unchanged (AC-11). The attendance side-effects (apply-to-log + status + history)
    /// run inside the runtime's transaction via the callbacks, so the workflow and attendance state commit
    /// atomically.
    /// </summary>
    private async Task<Result<RegularizationDecisionDto>?> TryWorkflowDecisionAsync(
        Guid regularizationId, WorkflowDecisionAction action, string? comment, CancellationToken cancellationToken)
    {
        if (_workflowRuntime is null || !_tenantContext.IsResolved)
            return null;

        var peek = await _dbContext.AttendanceRegularizations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == regularizationId, cancellationToken);
        if (peek is null || peek.WorkflowInstanceId is null)
            return null; // legacy single-level path (AC-11)

        AttendanceLog? approvedLog = null;

        var decision = await _workflowRuntime.DecideAsync(
            WorkflowEntityType.Attendance, regularizationId, action, comment,
            onApproved: async ct =>
            {
                var staged = await StageRegApprovalAsync(regularizationId, comment, ct);
                if (staged.IsFailure)
                    return Result.Failure(staged.Error!, staged.StatusCode ?? 400, staged.ErrorCode);
                approvedLog = staged.Value;
                return Result.Success();
            },
            onRejected: ct => StageRegRejectionAsync(regularizationId, comment, ct),
            cancellationToken: cancellationToken);

        if (decision.IsFailure)
            return Result<RegularizationDecisionDto>.Failure(
                decision.Error!, decision.StatusCode ?? 400, decision.ErrorCode);

        var outcome = decision.Value!;
        var now = DateTime.UtcNow;

        switch (outcome.Outcome)
        {
            case WorkflowDecisionOutcome.StepAdvanced:
            case WorkflowDecisionOutcome.StepRecorded:
                // Intermediate approval — the regularization stays Pending until the final step approves.
                return Result<RegularizationDecisionDto>.Success(new RegularizationDecisionDto
                {
                    RegularizationId = regularizationId,
                    Status = RegularizationStatus.Pending,
                    Action = RegularizationApprovalAction.Approved,
                    ApprovalLevel = outcome.NextStepOrder ?? 0,
                    ActionedAt = now,
                });

            case WorkflowDecisionOutcome.InstanceApproved:
                return Result<RegularizationDecisionDto>.Success(new RegularizationDecisionDto
                {
                    RegularizationId = regularizationId,
                    Status = RegularizationStatus.Approved,
                    Action = RegularizationApprovalAction.Approved,
                    ApprovalLevel = outcome.NextStepOrder ?? 1,
                    AttendanceLogId = approvedLog?.Id,
                    TotalWorkMinutes = approvedLog?.TotalWorkMinutes,
                    OvertimeMinutes = approvedLog?.OvertimeMinutes,
                    AttendanceStatus = approvedLog?.Status,
                    Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
                    ActionedAt = now,
                });

            case WorkflowDecisionOutcome.InstanceRejected:
            default:
                return Result<RegularizationDecisionDto>.Success(new RegularizationDecisionDto
                {
                    RegularizationId = regularizationId,
                    Status = RegularizationStatus.Rejected,
                    Action = RegularizationApprovalAction.Rejected,
                    ApprovalLevel = 1,
                    Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
                    ActionedAt = now,
                });
        }
    }

    /// <summary>
    /// Stages the approve side-effects (period-lock guard + apply-to-attendance-log + status → Approved +
    /// history + audit) WITHOUT saving — the workflow runtime owns the commit so the workflow completion and the
    /// attendance mutation are one atomic unit. Mirrors <see cref="ApproveCoreAsync"/> for the workflow path.
    /// The acting approver must have a linked employee record (the history FK requires it); otherwise the staged
    /// approval fails and the runtime rolls the transition back.
    /// </summary>
    private async Task<Result<AttendanceLog>> StageRegApprovalAsync(
        Guid regularizationId, string? comment, CancellationToken cancellationToken)
    {
        var regularization = await _dbContext.AttendanceRegularizations
            .FirstOrDefaultAsync(r => r.Id == regularizationId, cancellationToken);
        if (regularization is null)
            return Result<AttendanceLog>.Failure("Regularization request not found.", 404);

        var approver = await GetCurrentEmployeeAsync(cancellationToken);
        if (approver is null)
            return Result<AttendanceLog>.Failure(
                "The approving user has no linked employee record.", 403, "no_employee_record");

        // BR-5: re-check the period lock AT APPROVAL TIME (same guard as the legacy path).
        var locked = await _dbContext.AttendancePeriodLocks
            .AnyAsync(p => p.IsLocked && p.PeriodStart <= regularization.Date && p.PeriodEnd >= regularization.Date,
                cancellationToken);
        if (locked)
            return Result<AttendanceLog>.Failure(
                "This date falls within a locked payroll period. Please contact HR.", 409, "payroll_period_locked");

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        var logResult = await ApplyToAttendanceLogAsync(regularization, settings, cancellationToken);
        if (logResult.IsFailure)
            return Result<AttendanceLog>.Failure(
                logResult.Error!, logResult.StatusCode ?? 400, logResult.ErrorCode);

        var log = logResult.Value!;
        var beforeSnapshot = SnapshotRegularization(regularization);

        regularization.Status = RegularizationStatus.Approved;
        regularization.AttendanceLogId = log.Id;

        var history = NewHistory(
            regularization.Id, approver.Id, RegularizationApprovalAction.Approved,
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
        _dbContext.RegularizationApprovalHistories.Add(history);

        AddRegularizationAudit("AttendanceRegularization.Approved", regularization.Id,
            before: beforeSnapshot, after: SnapshotRegularization(regularization),
            detail: $"Approved via workflow by {approver.Id} → attendance_log {log.Id} "
                  + $"({log.TotalWorkMinutes} min, status {log.Status}).");

        // NO SaveChanges — the runtime commits this atomically with the workflow transition.
        return Result<AttendanceLog>.Success(log);
    }

    /// <summary>
    /// Stages the reject side-effect (status → Rejected + history + audit) WITHOUT saving — the runtime owns the
    /// commit. The approval-history row is only written when the acting user has a linked employee record (the
    /// history FK requires it); otherwise the rejection is still recorded on the regularization status + audit.
    /// </summary>
    private async Task StageRegRejectionAsync(
        Guid regularizationId, string? reason, CancellationToken cancellationToken)
    {
        var regularization = await _dbContext.AttendanceRegularizations
            .FirstOrDefaultAsync(r => r.Id == regularizationId, cancellationToken);
        if (regularization is null)
            return;

        var beforeSnapshot = SnapshotRegularization(regularization);
        regularization.Status = RegularizationStatus.Rejected;

        var approver = await GetCurrentEmployeeAsync(cancellationToken);
        if (approver is not null)
        {
            var history = NewHistory(
                regularization.Id, approver.Id, RegularizationApprovalAction.Rejected,
                string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
            _dbContext.RegularizationApprovalHistories.Add(history);
        }

        AddRegularizationAudit("AttendanceRegularization.Rejected", regularization.Id,
            before: beforeSnapshot, after: SnapshotRegularization(regularization),
            detail: $"Rejected via workflow. Reason: {reason?.Trim()}");
    }

    /// <summary>
    /// FR-2 / AC-1: create or update the attendance_log for the regularized employee + day with the
    /// requested clock-in/out, recalculating total_work_minutes / overtime / status via the shared
    /// <see cref="AttendanceCalculator"/>. Returns the tracked log (NOT yet saved — the caller commits
    /// it atomically with the regularization status + history). Does not call SaveChanges.
    ///
    /// Resolution of the corrected times by regularization type:
    ///   - MISSED_BOTH: use the request's clock-in and clock-out.
    ///   - MISSED_CLOCK_IN: requested clock-in + the existing log's clock-out (the punch they DID make).
    ///   - MISSED_CLOCK_OUT: the existing log's clock-in + the requested clock-out.
    /// When the regularization links no existing log (AttendanceLogId == null) a NEW log is created;
    /// otherwise the linked log is updated in place.
    /// </summary>
    private async Task<Result<AttendanceLog>> ApplyToAttendanceLogAsync(
        AttendanceRegularization regularization,
        AttendanceSettings settings,
        CancellationToken cancellationToken)
    {
        AttendanceLog? log = null;
        if (regularization.AttendanceLogId.HasValue)
        {
            log = await _dbContext.AttendanceLogs
                .FirstOrDefaultAsync(a => a.Id == regularization.AttendanceLogId.Value, cancellationToken);
        }

        // Resolve the effective clock-in/out, falling back to any existing punch on the linked log.
        var clockIn = regularization.RequestedClockIn ?? log?.ClockIn;
        var clockOut = regularization.RequestedClockOut ?? log?.ClockOut;

        if (clockIn is null || clockOut is null)
            return Result<AttendanceLog>.Failure(
                "The regularization is missing the clock-in or clock-out time needed to update the attendance record.",
                400, "incomplete_times");

        if (clockOut.Value <= clockIn.Value)
            return Result<AttendanceLog>.Failure(
                "The corrected clock-out must be after the corrected clock-in.", 400, "invalid_times");

        var calc = AttendanceCalculator.Calculate(clockIn.Value, clockOut.Value, settings);

        if (log is null)
        {
            // No existing record for the day (e.g. MISSED_BOTH with no prior punch) -> create one.
            log = new AttendanceLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = regularization.EmployeeId,
                Source = "WEB",
            };
            _dbContext.AttendanceLogs.Add(log);
        }

        log.ClockIn = clockIn.Value;
        log.ClockOut = clockOut.Value;
        log.TotalWorkMinutes = calc.TotalWorkMinutes;
        log.OvertimeMinutes = calc.OvertimeMinutes;
        log.Status = calc.Status;

        // US-ATT-008 BR-7: regularized records inherit late/early status from the REGULARIZED times, not
        // the original submission. Recompute against the shift effective on the regularized date so that
        // (e.g.) regularizing a late clock-in to an on-time value CLEARS the late flag. FLEXIBLE / no
        // shift → cleared (no tracking). Single source of truth: the same LateEarlyCalculator + the
        // same shift resolution the clock-in/out path uses.
        var tenantZone = await ResolveTenantTimeZoneAsync(cancellationToken);
        await RecomputeLateEarlyAsync(log, regularization.Date, settings, calc.TotalWorkMinutes, tenantZone, cancellationToken);

        return Result<AttendanceLog>.Success(log);
    }

    // ══════════════════════════════════════════════════════════════
    //  Shared load + authorization + state checks (FR-7/AC-5, BR-3/BR-6)
    // ══════════════════════════════════════════════════════════════

    private async Task<DecisionContext> LoadForDecisionAsync(
        Guid regularizationId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return DecisionContext.Fail("Tenant context is not resolved.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return DecisionContext.Fail("No employee record is linked to the current user.", 403);

        // Tracked load (we mutate Status). Tenant-scoped by the global query filter — a request from
        // another tenant is invisible (returns 404, never leaks cross-tenant existence).
        var regularization = await _dbContext.AttendanceRegularizations
            .FirstOrDefaultAsync(r => r.Id == regularizationId, cancellationToken);
        if (regularization is null)
            return DecisionContext.Fail("Regularization request not found.", 404);

        // BR-6: a manager cannot approve their OWN regularization — it must route to their supervisor
        // or HR. Checked BEFORE the team check so an unrelated 403 message isn't returned for self.
        if (regularization.EmployeeId == manager.Id)
            return DecisionContext.Fail(
                "You cannot approve your own regularization request. It must be approved by your manager or HR.",
                403, "self_approval");

        // FR-7 / AC-5: the manager may only act on requests from their direct reports. Exact story
        // message. Single-level direct-report scope (Phase 1); multi-level routing is deferred.
        var requester = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == regularization.EmployeeId, cancellationToken);
        if (requester is null || requester.ReportsToEmployeeId != manager.Id)
            return DecisionContext.Fail(
                "You are not authorized to approve requests for this employee.", 403, "not_team_member");

        // BR-3: an already APPROVED/REJECTED/CANCELLED request cannot be re-actioned (immutable).
        if (regularization.Status != RegularizationStatus.Pending)
            return DecisionContext.Fail(
                $"This request has already been actioned (status: {regularization.Status}).",
                409, "already_actioned");

        return new DecisionContext { Regularization = regularization, Manager = manager };
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees.FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    /// <summary>
    /// ISSUE-065: resolves the tenant's configured time zone into a <see cref="TimeZoneInfo"/> (UTC
    /// fallback via <see cref="TenantClock.ResolveTimeZone"/>) so the wall-clock late/early re-derivation
    /// runs in tenant-local terms. One DB read per approval.
    /// </summary>
    private async Task<TimeZoneInfo> ResolveTenantTimeZoneAsync(CancellationToken cancellationToken)
    {
        var tzId = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        return TenantClock.ResolveTimeZone(tzId, _logger);
    }

    /// <summary>
    /// Returns the tenant's attendance settings, creating a default (all enforcement off) row if none
    /// exists — the calculation policy thresholds the AttendanceCalculator reads. Matches the lazy
    /// creation in AttendanceService. The new row is committed immediately (it is not part of the
    /// approval's atomic unit and is harmless to persist on its own).
    /// </summary>
    private async Task<AttendanceSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AttendanceSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
            return settings;

        settings = new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
        };
        _dbContext.AttendanceSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    /// <summary>
    /// US-ATT-008 BR-7: recomputes and stamps the late/early fields on a (possibly newly created) log
    /// from its current clock-in/out against the shift effective on <paramref name="date"/>. Always sets
    /// all four fields (clears them when FLEXIBLE / no shift / on-time) so a regularization-to-on-time
    /// correctly clears a prior late flag. Uses the shared <see cref="LateEarlyCalculator"/> and the same
    /// US-ATT-005 resolution as the clock-in/out path (single source of truth). No SaveChanges here.
    /// </summary>
    private async Task RecomputeLateEarlyAsync(
        AttendanceLog log, DateOnly date, AttendanceSettings settings, int? workedMinutes,
        TimeZoneInfo tenantZone, CancellationToken cancellationToken)
    {
        log.IsLate = false;
        log.LateMinutes = 0;
        log.LateByMinutes = 0;
        log.IsEarlyDeparture = false;
        log.EarlyDepartureMinutes = 0;

        var resolved = await _shiftService.ResolveForEmployeeAsync(log.EmployeeId, date, cancellationToken);
        if (resolved.IsFailure || resolved.Value is null)
            return;

        var shift = resolved.Value;
        if (string.Equals(shift.Type, ShiftType.Flexible, StringComparison.OrdinalIgnoreCase))
            return;

        var start = TimeOnly.TryParse(shift.StartTime, out var st) ? st : (TimeOnly?)null;
        var end = TimeOnly.TryParse(shift.EndTime, out var en) ? en : (TimeOnly?)null;
        if (start is null && end is null)
            return;

        var grace = shift.GracePeriodMinutes > 0 ? shift.GracePeriodMinutes : settings.GracePeriodMinutes;
        var minimumMinutes = shift.MinimumHours is { } mh ? (int)Math.Round(mh * 60m) : 0;

        // ISSUE-065: late/early is a WALL-CLOCK comparison — derive the tenant-local time-of-day from the
        // stored UTC punch instants (UTC zone → no-op) before comparing against the shift start/end.
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: TenantClock.LocalTimeOfDay(log.ClockIn, tenantZone),
            clockOutTime: log.ClockOut is { } co ? TenantClock.LocalTimeOfDay(co, tenantZone) : null,
            shiftStart: start,
            shiftEnd: end,
            gracePeriodMinutes: Math.Max(0, grace),
            workedMinutes: workedMinutes,
            minimumMinutes: minimumMinutes);

        log.IsLate = result.IsLate;
        log.LateMinutes = result.LateMinutes;
        log.LateByMinutes = result.LateByMinutes;
        log.IsEarlyDeparture = result.IsEarlyDeparture;
        log.EarlyDepartureMinutes = result.EarlyDepartureMinutes;
    }

    // ── Audit (ISSUE-073) ──────────────────────────────────────────────
    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// ISSUE-073: appends a queryable tenant audit row (with before/after JSON snapshots) to the shared
    /// audit_log table for the approve/reject decision. Tenant/actor stamped from context (the actor is
    /// the acting manager). Mirrors <c>LeaveTypeService.AddLeaveTypeAudit</c>; added to the change tracker
    /// and persisted by the caller's SaveChanges (same transaction as the decision + history).
    /// </summary>
    private void AddRegularizationAudit(
        string action, Guid? resourceId, string? before, string? after, string detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "AttendanceRegularization",
            ResourceId = resourceId?.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string SnapshotRegularization(AttendanceRegularization r) => JsonSerializer.Serialize(new
    {
        r.EmployeeId,
        r.Date,
        r.RegularizationType,
        r.Status,
        r.AttendanceLogId,
        r.RequestedClockIn,
        r.RequestedClockOut,
    }, AuditJsonOptions);

    private RegularizationApprovalHistory NewHistory(
        Guid regularizationId, Guid approverEmployeeId, string action, string? comment) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        RegularizationId = regularizationId,
        ApproverEmployeeId = approverEmployeeId,
        ApprovalLevel = 1,  // single-level only (FR-4/AC-4 multi-level deferred to US-ADM-007)
        Action = action,
        Comment = comment,
        ActionedAt = DateTime.UtcNow,
    };

    private sealed class DecisionContext
    {
        public AttendanceRegularization? Regularization { get; init; }
        public Employee? Manager { get; init; }
        public Result<RegularizationDecisionDto>? Failure { get; init; }

        public static DecisionContext Fail(string error, int statusCode, string? errorCode = null) => new()
        {
            Failure = Result<RegularizationDecisionDto>.Failure(error, statusCode, errorCode),
        };
    }
}
