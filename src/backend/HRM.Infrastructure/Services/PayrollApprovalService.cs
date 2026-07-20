using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PA = HRM.Domain.Payroll.PayrollAuditAction;

namespace HRM.Infrastructure.Services;

/// <summary>
/// The payroll-run approval workflow (US-PAY-008). Implements the additive approval state machine over the
/// existing US-PAY-003 run lifecycle (BR-4). No shared approval-workflow engine exists (technical doc
/// section 34 / US-ADM-007 is not built), so this is a payroll-specific flow mirroring the established
/// leave/attendance approval-history pattern (LeaveApprovalHistory / RegularizationApprovalHistory).
///
/// <para>All queries are tenant-scoped via ITenantContext + EF global query filters (BR-8). Each action
/// writes one immutable <see cref="PayrollApprovalHistory"/> row (FR-7/NFR-5) and commits it atomically with
/// the run-status change, then fires the log-only notification seam (real SignalR/email deferred — US-NTF).</para>
///
/// <para>STATE MACHINE (rejects every other transition with a 409):
///   ReviewPending  --Submit-->        AwaitingApproval   (AC-1)
///   AwaitingApproval --Approve-->      AwaitingApproval (advance step) | Approved (all steps done) (AC-2/AC-4)
///   AwaitingApproval --Reject-->       Rejected           (AC-3, reason required)
///   AwaitingApproval --Return-->       ReviewPending      (FR-9, comments required)
///   Rejected/ReviewPending --Submit--> AwaitingApproval   (re-submit = new instance, BR-3)
///   Approved --Finalize-->             Finalized          (AC-5, terminal; BR-1 blocks no-approval finalize).</para>
/// </summary>
public sealed class PayrollApprovalService : IPayrollApprovalService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPayrollNotificationService _notifications;
    private readonly IPayrollAuditLogger _audit;
    private readonly ILogger<PayrollApprovalService> _logger;
    private readonly TimeProvider _timeProvider;

    private const int MinReasonLength = 10;

    public PayrollApprovalService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPayrollNotificationService notifications,
        IPayrollAuditLogger audit,
        ILogger<PayrollApprovalService> logger,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _audit = audit;
        _logger = logger;
        // US-PAY-008 FR-3 (ISSUE-173): injectable clock so the SlaDueAt stamping is deterministically testable
        // (a test can force a breach). Trailing-optional + TimeProvider.System default so the existing manual
        // test-DI constructions of this service keep compiling; DI supplies the real provider (DF-43).
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>US-PAY-012: audited snapshot of a run's status/totals/actor fields (before/after JSON).</summary>
    private static object RunSnapshot(PayrollRun r) => new
    {
        Status = r.Status.ToString(), r.PayMonth, r.PayYear, r.TotalEmployees,
        r.TotalGross, r.TotalDeductions, r.TotalNet,
        r.SubmittedBy, r.ApprovedBy, r.RejectionReason,
    };

    // ══════════════════════════════════════════════════════════════
    //  Submit for approval (AC-1, BR-3 re-submit)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PayrollApprovalResultDto>> SubmitForApprovalAsync(
        Guid runId, int? totalApprovalSteps, string? comments, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var load = await LoadRunAsync(runId, cancellationToken);
        if (load.Failure is not null) return load.Failure;
        var run = load.Run!;

        // BR-4: submit is allowed from ReviewPending (first submission) or Rejected/ReviewPending after a
        // correction (BR-3 — re-submit starts a NEW workflow instance).
        if (run.Status is not (PayrollRunStatus.ReviewPending or PayrollRunStatus.Rejected))
            return InvalidTransition(run.Status, "submitted for approval");

        // BUG-076 (FR-2): when the tenant has configured a step→role chain, the configured step COUNT is
        // authoritative — it overrides any caller-supplied totalApprovalSteps. With no config, keep the legacy
        // behaviour (caller value, BR-2 default single-step).
        int configuredSteps = await _dbContext.PayrollApprovalStepConfigs.AsNoTracking().CountAsync(cancellationToken);
        int steps = configuredSteps >= 1
            ? configuredSteps
            : (totalApprovalSteps is { } s && s > 0 ? s : 1);

        // BR-3: every submission is a fresh workflow instance.
        var instanceId = BaseEntity.NewUuidV7();
        run.Status = PayrollRunStatus.AwaitingApproval;
        run.CurrentWorkflowInstanceId = instanceId;
        run.CurrentApprovalStep = 1;
        run.TotalApprovalSteps = steps;
        run.SubmittedBy = _currentUser.UserId;
        run.SubmittedAt = DateTime.UtcNow;
        run.RejectionReason = null;  // a re-submit clears the prior rejection reason.

        // FR-3 (ISSUE-173): stamp the step-1 SLA (from its config's SlaHours) and start the step with a clean
        // escalation slate. Null when step 1 has no SLA configured (then the run never escalates).
        run.SlaDueAt = await ComputeSlaDueAtAsync(1, cancellationToken);
        run.EscalatedAt = null;

        // FR-6 (ISSUE-173): evaluate step-1 delegation at activation — if the step's primary approver is on approved
        // leave today the delegate becomes the effective approver (stamps DelegatedToUserId + adds a Delegated history
        // row to THIS SaveChanges). Returns whether it fired so the delegate is notified AFTER the run is persisted.
        bool delegated = await ApplyDelegationAsync(run, 1, cancellationToken);

        var history = NewHistory(run, instanceId, 1, PayrollApprovalAction.Submitted, Trim(comments), ipAddress);
        _dbContext.PayrollApprovalHistories.Add(history);

        // US-PAY-012 (FR-2): structured audit alongside the PayrollApprovalHistory row.
        _audit.Log(PA.PayrollRunSubmittedForApproval, PA.ResourceType.PayrollRun,
            run.Id.ToString(), before: null, after: RunSnapshot(run), ipAddress: ipAddress);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AC-1: notify the designated approver(s). Log-only seam; SignalR/email deferred.
        await _notifications.NotifyApprovalEventAsync(_tenantContext.TenantId, run.Id, "payroll-approval-submitted", cancellationToken);

        // FR-6: notify the delegate AFTER the run is persisted (the Real service reads the just-saved DelegatedToUserId).
        if (delegated)
            await _notifications.NotifyApprovalEventAsync(_tenantContext.TenantId, run.Id, "payroll-approval-delegated", cancellationToken);

        Log(run, PayrollApprovalAction.Submitted);
        return Ok(run, PayrollApprovalAction.Submitted);
    }

    // ══════════════════════════════════════════════════════════════
    //  Approve (AC-2, AC-4 multi-step, BR-5 maker-checker)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PayrollApprovalResultDto>> ApproveAsync(
        Guid runId, string? comments, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var load = await LoadRunAsync(runId, cancellationToken);
        if (load.Failure is not null) return load.Failure;
        var run = load.Run!;

        if (run.Status != PayrollRunStatus.AwaitingApproval)
            return InvalidTransition(run.Status, "approved");

        // BR-5 maker-checker: the submitter cannot approve their own run, UNLESS the tenant has fewer than 2
        // eligible approvers (small-team exception). "Eligible approver" = an active tenant user whose roles
        // grant the Payroll.Approve permission (the same gate the controller enforces).
        if (run.SubmittedBy == _currentUser.UserId)
        {
            var eligibleApprovers = await CountEligibleApproversAsync(cancellationToken);
            if (eligibleApprovers >= 2)
                return Result<PayrollApprovalResultDto>.Failure(
                    "You cannot approve a payroll run that you submitted. It must be approved by another authorized approver (maker-checker).",
                    403, "self_approval");
            // else: small-team exception — fall through and allow the self-approval.
        }

        int step = run.CurrentApprovalStep ?? 1;
        int total = run.TotalApprovalSteps ?? 1;
        var instanceId = run.CurrentWorkflowInstanceId ?? BaseEntity.NewUuidV7();

        // BUG-076 AC-4 (separation of duties): the CORE fix. Whenever a run needs more than one approval, a
        // user who already recorded an Approved decision for THIS workflow instance cannot approve another step.
        // Applies regardless of whether a step→role config exists (a numeric multi-step run is also protected).
        if (total > 1 && await HasCallerAlreadyApprovedAsync(instanceId, cancellationToken))
            return Result<PayrollApprovalResultDto>.Failure(
                "Each approval step must be completed by a different person (separation of duties).",
                403, "distinct_approver_required");

        // BUG-076 FR-2 (step-role binding): when this step has a configured approver role, the acting user must
        // hold that tenant Role (the same UserTenant→UserTenantRole join used for eligibility). No config row for
        // the step = no per-step role restriction (legacy numeric behaviour).
        var stepConfig = await _dbContext.PayrollApprovalStepConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.StepNumber == step, cancellationToken);
        if (stepConfig is not null && !await UserHoldsRoleAsync(stepConfig.RoleId, cancellationToken))
            return Result<PayrollApprovalResultDto>.Failure(
                "You are not the assigned approver role for this step.", 403, "not_step_approver");

        var beforeApprove = RunSnapshot(run);

        var history = NewHistory(run, instanceId, step, PayrollApprovalAction.Approved, Trim(comments), ipAddress);
        _dbContext.PayrollApprovalHistories.Add(history);

        // FR-6 (ISSUE-173): set when the step-advance below delegates the NEXT step (notify after the run is persisted).
        bool delegated = false;

        if (step >= total)
        {
            // AC-2 / AC-4: all steps complete — the run is Approved.
            run.Status = PayrollRunStatus.Approved;
            run.ApprovedBy = _currentUser.UserId;
            run.ApprovedAt = DateTime.UtcNow;
            run.CurrentApprovalStep = null;

            // FR-3 (ISSUE-173): the run has left AwaitingApproval — clear the SLA slate so the escalator never
            // picks it up.
            run.SlaDueAt = null;
            run.EscalatedAt = null;

            // FR-6 hygiene: the run is no longer awaiting a step approver — clear the effective delegate.
            run.DelegatedToUserId = null;

            // US-PAY-012 (FR-2/BR-5): audit the terminal Approved transition WITH IP/user-agent (sensitive
            // action). Intermediate step approvals are captured in PayrollApprovalHistory; the PayrollRun.Approved
            // audit action fires once on the final approval (matches the §7 single action value).
            _audit.Log(PA.PayrollRunApproved, PA.ResourceType.PayrollRun,
                run.Id.ToString(), before: beforeApprove, after: RunSnapshot(run), ipAddress: ipAddress);
        }
        else
        {
            // AC-4: advance to the next step; the run stays AwaitingApproval until the last step approves.
            run.CurrentApprovalStep = step + 1;

            // FR-3 (ISSUE-173): a fresh step gets a fresh SLA — stamp from the NEXT step's config (null when it has
            // no SLA) and clear the escalation slate so this step can escalate independently.
            run.SlaDueAt = await ComputeSlaDueAtAsync(step + 1, cancellationToken);
            run.EscalatedAt = null;

            // FR-6 (ISSUE-173): evaluate the NEXT step's delegation at its activation — primary-on-leave → delegate is
            // the effective approver (stamps DelegatedToUserId + Delegated history row into this SaveChanges). Notified
            // after persistence.
            delegated = await ApplyDelegationAsync(run, step + 1, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyApprovalEventAsync(_tenantContext.TenantId, run.Id, "payroll-approval-approved", cancellationToken);

        // FR-6: notify the newly-activated step's delegate AFTER the run is persisted (recipient = the saved DelegatedToUserId).
        if (delegated)
            await _notifications.NotifyApprovalEventAsync(_tenantContext.TenantId, run.Id, "payroll-approval-delegated", cancellationToken);

        Log(run, PayrollApprovalAction.Approved);
        return Ok(run, PayrollApprovalAction.Approved);
    }

    // ══════════════════════════════════════════════════════════════
    //  Reject (AC-3 — reason required)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PayrollApprovalResultDto>> RejectAsync(
        Guid runId, string reason, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var trimmed = Trim(reason) ?? string.Empty;
        if (trimmed.Length < MinReasonLength)
            return Result<PayrollApprovalResultDto>.Failure(
                $"A rejection reason of at least {MinReasonLength} characters is required.", 400, "reason_required");

        var load = await LoadRunAsync(runId, cancellationToken);
        if (load.Failure is not null) return load.Failure;
        var run = load.Run!;

        if (run.Status != PayrollRunStatus.AwaitingApproval)
            return InvalidTransition(run.Status, "rejected");

        int step = run.CurrentApprovalStep ?? 1;
        var instanceId = run.CurrentWorkflowInstanceId ?? BaseEntity.NewUuidV7();

        var beforeReject = RunSnapshot(run);

        // AC-3: status -> Rejected, store the reason. HR corrects + re-submits (BR-3, new instance).
        run.Status = PayrollRunStatus.Rejected;
        run.RejectionReason = trimmed;
        run.SlaDueAt = null;   // FR-3 hygiene: a rejected run has no live approval SLA (re-submit re-stamps).
        run.EscalatedAt = null;
        run.DelegatedToUserId = null;   // FR-6 hygiene: no active step → no effective delegate.
        run.CurrentApprovalStep = null;

        var history = NewHistory(run, instanceId, step, PayrollApprovalAction.Rejected, trimmed, ipAddress);
        _dbContext.PayrollApprovalHistories.Add(history);

        // US-PAY-012 (FR-2): audit the rejection with before/after JSON + the IP.
        _audit.Log(PA.PayrollRunRejected, PA.ResourceType.PayrollRun,
            run.Id.ToString(), before: beforeReject, after: RunSnapshot(run), ipAddress: ipAddress);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyApprovalEventAsync(_tenantContext.TenantId, run.Id, "payroll-approval-rejected", cancellationToken);

        Log(run, PayrollApprovalAction.Rejected);
        return Ok(run, PayrollApprovalAction.Rejected);
    }

    // ══════════════════════════════════════════════════════════════
    //  Return to HR (FR-9 — comments required, no formal rejection)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PayrollApprovalResultDto>> ReturnToHrAsync(
        Guid runId, string comments, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var trimmed = Trim(comments) ?? string.Empty;
        if (trimmed.Length < MinReasonLength)
            return Result<PayrollApprovalResultDto>.Failure(
                $"Return comments of at least {MinReasonLength} characters are required.", 400, "comments_required");

        var load = await LoadRunAsync(runId, cancellationToken);
        if (load.Failure is not null) return load.Failure;
        var run = load.Run!;

        if (run.Status != PayrollRunStatus.AwaitingApproval)
            return InvalidTransition(run.Status, "returned to HR");

        int step = run.CurrentApprovalStep ?? 1;
        var instanceId = run.CurrentWorkflowInstanceId ?? BaseEntity.NewUuidV7();

        // FR-9: send back to HR without a formal rejection. Closes the active workflow instance; a subsequent
        // submit starts a new one (mirrors the BR-3 re-submit semantics).
        run.Status = PayrollRunStatus.ReviewPending;
        run.SlaDueAt = null;   // FR-3 hygiene: no live approval SLA while back with HR (re-submit re-stamps).
        run.EscalatedAt = null;
        run.DelegatedToUserId = null;   // FR-6 hygiene: no active step → no effective delegate.
        run.CurrentWorkflowInstanceId = null;
        run.CurrentApprovalStep = null;
        run.TotalApprovalSteps = null;

        var history = NewHistory(run, instanceId, step, PayrollApprovalAction.Returned, trimmed, ipAddress);
        _dbContext.PayrollApprovalHistories.Add(history);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyApprovalEventAsync(_tenantContext.TenantId, run.Id, "payroll-approval-returned", cancellationToken);

        Log(run, PayrollApprovalAction.Returned);
        return Ok(run, PayrollApprovalAction.Returned);
    }

    // ══════════════════════════════════════════════════════════════
    //  Finalize (AC-5, BR-1 require approval first, BR-6 terminal)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PayrollApprovalResultDto>> FinalizeAsync(
        Guid runId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var load = await LoadRunAsync(runId, cancellationToken);
        if (load.Failure is not null) return load.Failure;
        var run = load.Run!;

        // BR-6: Finalized is terminal/irreversible.
        if (run.Status == PayrollRunStatus.Finalized)
            return Result<PayrollApprovalResultDto>.Failure(
                "This payroll run is already finalized.", 409, "already_finalized");

        // AC-5/BR-1: only an Approved run can be finalized. A direct ReviewPending -> Finalized (no approval
        // step) is explicitly blocked — a run MUST go through at least one approval.
        if (run.Status != PayrollRunStatus.Approved)
            return Result<PayrollApprovalResultDto>.Failure(
                "A payroll run must be approved before it can be finalized.", 409, "approval_required");

        var beforeFinalize = RunSnapshot(run);
        run.Status = PayrollRunStatus.Finalized;
        run.FinalizedAt = DateTime.UtcNow;
        run.CurrentApprovalStep = null;

        // US-PAY-012 (FR-2/BR-5): finalize is the most sensitive action — audit with before/after + IP/user-agent
        // (non-repudiation). This is a STRUCTURED audit entry distinct from PayrollApprovalHistory (which records
        // no finalize row per the §7 action set).
        _audit.Log(PA.PayrollRunFinalized, PA.ResourceType.PayrollRun,
            run.Id.ToString(), before: beforeFinalize, after: RunSnapshot(run), ipAddress: ipAddress);

        // FR-8: lock all payslips for the run immutable. The slips become read-only by virtue of the run being
        // Finalized (US-PAY-005 list/detail already gate on Finalized, and the processor refuses to reprocess a
        // Finalized run, US-PAY-003 BR-7) — there is no per-slip mutable flag to flip, so the Finalized run
        // status IS the immutability lock. No history row required for finalize per the §7 action set, but we
        // record one for a complete audit trail (FR-7) using the last instance.
        var instanceId = run.CurrentWorkflowInstanceId ?? BaseEntity.NewUuidV7();
        // Finalize is not in the §7 action enum; the audit trail captures it as an Approved-instance closure
        // via the run's FinalizedAt. We intentionally do NOT invent a new action value to keep the wire enum
        // aligned with the data spec.

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyApprovalEventAsync(_tenantContext.TenantId, run.Id, "payroll-finalized", cancellationToken);

        _logger.LogInformation(
            "Payroll run {RunId} FINALIZED by {User} in tenant {TenantId}. Instance {Instance}.",
            run.Id, _currentUser.UserId, _tenantContext.TenantId, instanceId);

        return Ok(run, "Finalized");
    }

    // ══════════════════════════════════════════════════════════════
    //  Reads — history (FR-7) + summary (FR-4)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<PayrollApprovalHistoryDto>>> GetHistoryAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayrollApprovalHistoryDto>>.Failure("Tenant context is not resolved.", 400);

        var exists = await _dbContext.PayrollRuns.AsNoTracking().AnyAsync(r => r.Id == runId, cancellationToken);
        if (!exists)
            return Result<IReadOnlyList<PayrollApprovalHistoryDto>>.Failure("Payroll run not found.", 404, "run_not_found");

        var rows = await _dbContext.PayrollApprovalHistories.AsNoTracking()
            .Where(h => h.PayrollRunId == runId)
            .OrderByDescending(h => h.ActedAt)
            .Select(h => new PayrollApprovalHistoryDto
            {
                Id = h.Id,
                PayrollRunId = h.PayrollRunId,
                WorkflowInstanceId = h.WorkflowInstanceId,
                StepNumber = h.StepNumber,
                Action = h.Action,
                ActorUserId = h.ActorUserId,
                Comments = h.Comments,
                ActedAt = h.ActedAt,
                IpAddress = h.IpAddress,
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PayrollApprovalHistoryDto>>.Success(rows);
    }

    public async Task<Result<PayrollApprovalSummaryDto>> GetSummaryAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollApprovalSummaryDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayrollApprovalSummaryDto>.Failure("Payroll run not found.", 404, "run_not_found");

        // FR-4: previous-month baseline = the most recent FINALIZED run for the tenant in an EARLIER period
        // than this run. Tenant-scoped by the global query filter (BR-8).
        var previous = await _dbContext.PayrollRuns.AsNoTracking()
            .Where(r => r.Status == PayrollRunStatus.Finalized
                && (r.PayYear < run.PayYear || (r.PayYear == run.PayYear && r.PayMonth < run.PayMonth)))
            .OrderByDescending(r => r.PayYear).ThenByDescending(r => r.PayMonth)
            .FirstOrDefaultAsync(cancellationToken);

        decimal? previousNet = previous?.TotalNet;
        decimal? variance = previousNet is { } p && p != 0m
            ? Math.Round((run.TotalNet - p) / p * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        // Exceptions (FR-4): warnings the approver should review. Built from stored counters + per-slip checks.
        var exceptions = new List<string>();
        if (run.SkippedEmployees > 0)
            exceptions.Add($"{run.SkippedEmployees} employee(s) were skipped (e.g. missing salary structure). See the run log.");

        var negativeNetCount = await _dbContext.PayrollSlips.AsNoTracking()
            .CountAsync(s => s.PayrollRunId == runId && s.NetSalary < 0m, cancellationToken);
        if (negativeNetCount > 0)
            exceptions.Add($"{negativeNetCount} payslip(s) have a negative net salary.");

        if (run.ProcessedEmployees == 0)
            exceptions.Add("No employees were processed in this run.");

        return Result<PayrollApprovalSummaryDto>.Success(new PayrollApprovalSummaryDto
        {
            RunId = run.Id,
            PayMonth = run.PayMonth,
            PayYear = run.PayYear,
            Status = run.Status.ToString(),
            TotalEmployees = run.TotalEmployees,
            TotalGross = run.TotalGross,
            TotalDeductions = run.TotalDeductions,
            TotalStatutory = run.TotalStatutory,
            TotalNet = run.TotalNet,
            PreviousMonthTotalNet = previousNet,
            VariancePercentage = variance,
            Exceptions = exceptions,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Pending approvals for the current approver (DF-14)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// DF-14: the AwaitingApproval runs the CURRENT caller can approve right now. The include rule mirrors
    /// <see cref="ApproveAsync"/> exactly so the queue and the approve action never drift — a run appears here
    /// iff calling approve on it would NOT 403:
    /// <list type="number">
    /// <item>Step-role (AC-4/FR-2): if the run's current step has a configured role, include only when the caller
    /// holds it (<see cref="UserHoldsRoleAsync"/>); no config row = no per-step restriction.</item>
    /// <item>Maker-checker (BR-5): exclude the caller's own submissions unless the tenant has fewer than 2
    /// eligible approvers (<see cref="CountEligibleApproversAsync"/>, small-team exception).</item>
    /// <item>Distinct-approver (AC-4): for multi-step runs, exclude if the caller already recorded an Approved
    /// decision for this workflow instance (<see cref="HasCallerAlreadyApprovedAsync"/>).</item>
    /// </list>
    /// Tenant-scoped by the EF global query filter (BR-8); ordered newest-first.
    /// </summary>
    public async Task<Result<IReadOnlyList<PendingApprovalDto>>> GetPendingApprovalsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PendingApprovalDto>>.Failure("Tenant context is not resolved.", 400);

        var callerId = _currentUser.UserId;

        // All AwaitingApproval runs for the tenant (global filter → tenant-scoped, BR-8), newest period first.
        var runs = await _dbContext.PayrollRuns.AsNoTracking()
            .Where(r => r.Status == PayrollRunStatus.AwaitingApproval)
            .OrderByDescending(r => r.PayYear).ThenByDescending(r => r.PayMonth)
            .ToListAsync(cancellationToken);

        if (runs.Count == 0)
            return Result<IReadOnlyList<PendingApprovalDto>>.Success([]);

        // Step → role config keyed by step number (mirrors ApproveAsync's per-step role gate, :168-172).
        var stepRoleByStep = await _dbContext.PayrollApprovalStepConfigs.AsNoTracking()
            .ToDictionaryAsync(c => c.StepNumber, c => c.RoleId, cancellationToken);

        // BR-5 small-team exception (computed once; :137-142). Fewer than 2 eligible approvers relaxes maker-checker.
        bool smallTeam = await CountEligibleApproversAsync(cancellationToken) < 2;

        // Reuse UserHoldsRoleAsync per distinct step role, cached so N runs at the same step cost one query.
        var roleHeld = new Dictionary<Guid, bool>();
        async Task<bool> HoldsRoleAsync(Guid roleId)
        {
            if (!roleHeld.TryGetValue(roleId, out var held))
            {
                held = await UserHoldsRoleAsync(roleId, cancellationToken);
                roleHeld[roleId] = held;
            }
            return held;
        }

        var included = new List<PayrollRun>(runs.Count);
        foreach (var run in runs)
        {
            // (1) Step-role: a configured step role must be held by the caller (:168-172).
            int step = run.CurrentApprovalStep ?? 1;
            if (stepRoleByStep.TryGetValue(step, out var roleId) && !await HoldsRoleAsync(roleId))
                continue;

            // (2) Maker-checker: exclude own submissions unless the small-team exception applies (:135-143).
            if (run.SubmittedBy == callerId && !smallTeam)
                continue;

            // (3) Distinct-approver: multi-step runs the caller already approved are excluded (:152-163).
            int total = run.TotalApprovalSteps ?? 1;
            if (total > 1 && run.CurrentWorkflowInstanceId is { } instanceId
                && await HasCallerAlreadyApprovedAsync(instanceId, cancellationToken))
                continue;

            included.Add(run);
        }

        // Resolve submitter display names via a batched keyed lookup — NO N+1 (mirrors the role-name lookup style
        // in GetApprovalStepConfigAsync :444-458).
        var submitterIds = included.Where(r => r.SubmittedBy is not null)
            .Select(r => r.SubmittedBy!.Value).Distinct().ToList();
        var nameByUserId = await ResolveSubmitterNamesAsync(submitterIds, cancellationToken);

        var rows = included.Select(r => new PendingApprovalDto
        {
            RunId = r.Id,
            PayMonth = r.PayMonth,
            PayYear = r.PayYear,
            Status = r.Status.ToString(),
            ProcessedEmployees = r.ProcessedEmployees,
            TotalEmployees = r.TotalEmployees,
            TotalGross = r.TotalGross,
            TotalNet = r.TotalNet,
            SubmittedBy = r.SubmittedBy ?? Guid.Empty,
            InitiatedByName = r.SubmittedBy is { } sid && nameByUserId.TryGetValue(sid, out var name)
                ? name : string.Empty,
            InitiatedAt = r.InitiatedAt,
            CurrentApprovalStep = r.CurrentApprovalStep,
            TotalApprovalSteps = r.TotalApprovalSteps,
        }).ToList();

        return Result<IReadOnlyList<PendingApprovalDto>>.Success(rows);
    }

    /// <summary>
    /// DF-14: batched keyed lookup of submitter display names (no N+1). Prefers the tenant Employee linked to the
    /// user (First Last); falls back to the global <see cref="User.DisplayName"/>, then email, for submitters with
    /// no linked employee. Employees are tenant-scoped by the global filter; Users are keyed by explicit ids.
    /// </summary>
    private async Task<Dictionary<Guid, string>> ResolveSubmitterNamesAsync(
        IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        if (userIds.Count == 0) return result;

        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.UserId != null && userIds.Contains(e.UserId!.Value))
            .Select(e => new { e.UserId, e.FirstName, e.LastName })
            .ToListAsync(cancellationToken);
        foreach (var e in employees)
            if (e.UserId is { } uid && !result.ContainsKey(uid))
                result[uid] = $"{e.FirstName} {e.LastName}".Trim();

        var missing = userIds.Where(id => !result.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var users = await _dbContext.Users.AsNoTracking()
                .Where(u => missing.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName, u.Email })
                .ToListAsync(cancellationToken);
            foreach (var u in users)
                result[u.Id] = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName! : u.Email;
        }

        return result;
    }

    // ══════════════════════════════════════════════════════════════
    //  Configurable step → role approval config (AC-4/FR-2, BUG-076)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<PayrollApprovalStepConfigDto>>> GetApprovalStepConfigAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure("Tenant context is not resolved.", 400);

        // Role names are resolved via a separate keyed lookup rather than a nav/GroupJoin projection — the
        // latter silently empties on the EF InMemory provider used by the unit/integration suites.
        var configs = await _dbContext.PayrollApprovalStepConfigs.AsNoTracking()
            .OrderBy(c => c.StepNumber)
            .ToListAsync(cancellationToken);

        // Include backup role ids in the same keyed name lookup so both the primary + backup role names resolve
        // in one query (FR-3, ISSUE-173).
        var roleIds = configs.Select(c => c.RoleId)
            .Concat(configs.Where(c => c.BackupRoleId is not null).Select(c => c.BackupRoleId!.Value))
            .Distinct().ToList();
        var roleNames = await _dbContext.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var rows = configs.Select(c => new PayrollApprovalStepConfigDto
        {
            StepNumber = c.StepNumber,
            RoleId = c.RoleId,
            RoleName = roleNames.TryGetValue(c.RoleId, out var name) ? name : string.Empty,
            SlaHours = c.SlaHours,
            BackupRoleId = c.BackupRoleId,
            BackupRoleName = c.BackupRoleId is { } bid && roleNames.TryGetValue(bid, out var bname) ? bname : null,
            PrimaryApproverUserId = c.PrimaryApproverUserId,
            DelegateUserId = c.DelegateUserId,
        }).ToList();

        return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Success(rows);
    }

    public async Task<Result<IReadOnlyList<PayrollApprovalStepConfigDto>>> SetApprovalStepConfigAsync(
        IReadOnlyList<PayrollApprovalStepConfigItem> steps, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure("Tenant context is not resolved.", 400);

        if (steps is null || steps.Count == 0)
            return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure(
                "At least one approval step is required.", 400, "steps_required");

        // Steps must be contiguous 1..N (no gaps, no duplicates) once ordered by step number.
        var ordered = steps.OrderBy(s => s.StepNumber).ToList();
        for (int i = 0; i < ordered.Count; i++)
            if (ordered[i].StepNumber != i + 1)
                return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure(
                    "Approval steps must be contiguous starting at 1 (1..N with no gaps or duplicates).",
                    400, "steps_not_contiguous");

        // FR-3 (ISSUE-173): SlaHours is opt-in but, when set, must be a positive number of hours.
        if (ordered.Any(s => s.SlaHours is <= 0))
            return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure(
                "SlaHours, when set, must be greater than 0.", 400, "sla_hours_invalid");

        // Primary role ids PLUS any backup role ids (FR-3): backups get the SAME tenant-existence + Payroll.Approve
        // validation as the primary step role — a backup that can't approve would be a dead escalation target.
        var roleIds = ordered.Select(s => s.RoleId)
            .Concat(ordered.Where(s => s.BackupRoleId is not null).Select(s => s.BackupRoleId!.Value))
            .Distinct().ToList();

        // Every role must exist IN THIS TENANT (tenant-owned role, not a null/system role).
        var tenantRoleIds = await _dbContext.Roles.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && roleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (roleIds.Any(id => !tenantRoleIds.Contains(id)))
            return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure(
                "One or more step or backup roles do not exist in this tenant.", 400, "role_not_found");

        // Every role must hold Payroll.Approve — a step/backup role that can't pass the controller's approve gate
        // would be a dead misconfiguration (nobody in it could ever approve the step).
        var approveRoleIds = await _dbContext.RolePermissions.AsNoTracking()
            .Where(rp => rp.Permission == PermissionCatalog.Payroll.Approve && roleIds.Contains(rp.RoleId))
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (roleIds.Any(id => !approveRoleIds.Contains(id)))
            return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure(
                "Every step and backup role must hold the Payroll.Approve permission.", 400, "role_missing_approve_permission");

        // FR-6 (ISSUE-173): per-step delegation. A step opts in by setting BOTH a primary approver + a delegate user;
        // setting only one is a mis-config (400). Both, when set, must be ACTIVE in-tenant users holding Payroll.Approve
        // (same gate as the step/backup roles) — a delegate who cannot pass the approve gate would be a dead target.
        if (ordered.Any(s => s.PrimaryApproverUserId is not null ^ s.DelegateUserId is not null))
            return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure(
                "A delegation step must set both a primary approver and a delegate user.", 400, "delegation_config_incomplete");

        var delegationUserIds = ordered
            .SelectMany(s => new[] { s.PrimaryApproverUserId, s.DelegateUserId })
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (delegationUserIds.Count > 0)
        {
            // ACTIVE in-tenant users (UserTenant is not BaseEntity → filter tenant + status explicitly).
            var activeTenantUserIds = await _dbContext.UserTenants.AsNoTracking()
                .Where(ut => ut.TenantId == _tenantContext.TenantId && ut.Status == UserTenantStatus.Active
                    && delegationUserIds.Contains(ut.UserId))
                .Select(ut => ut.UserId).Distinct()
                .ToListAsync(cancellationToken);
            if (delegationUserIds.Any(id => !activeTenantUserIds.Contains(id)))
                return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure(
                    "One or more delegation users are not active users in this tenant.", 400, "delegation_user_not_found");

            // …and each must hold Payroll.Approve (the same UserTenant→role→permission join as CountEligibleApprovers).
            var approveUserIds = await (
                from ut in _dbContext.UserTenants.AsNoTracking()
                where ut.TenantId == _tenantContext.TenantId && ut.Status == UserTenantStatus.Active
                    && delegationUserIds.Contains(ut.UserId)
                join utr in _dbContext.UserTenantRoles.AsNoTracking() on ut.Id equals utr.UserTenantId
                join rp in _dbContext.RolePermissions.AsNoTracking() on utr.RoleId equals rp.RoleId
                where rp.Permission == PermissionCatalog.Payroll.Approve
                select ut.UserId).Distinct().ToListAsync(cancellationToken);
            if (delegationUserIds.Any(id => !approveUserIds.Contains(id)))
                return Result<IReadOnlyList<PayrollApprovalStepConfigDto>>.Failure(
                    "Every delegation primary approver and delegate must hold the Payroll.Approve permission.",
                    400, "delegation_user_missing_approve_permission");
        }

        // Atomic replace: drop the tenant's existing config and insert the new ordered set in one SaveChanges.
        var existing = await _dbContext.PayrollApprovalStepConfigs.ToListAsync(cancellationToken);
        var before = existing.OrderBy(c => c.StepNumber)
            .Select(c => new { c.StepNumber, c.RoleId, c.SlaHours, c.BackupRoleId, c.PrimaryApproverUserId, c.DelegateUserId }).ToList();
        _dbContext.PayrollApprovalStepConfigs.RemoveRange(existing);

        foreach (var s in ordered)
            _dbContext.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                StepNumber = s.StepNumber,
                RoleId = s.RoleId,
                SlaHours = s.SlaHours,
                BackupRoleId = s.BackupRoleId,
                PrimaryApproverUserId = s.PrimaryApproverUserId,
                DelegateUserId = s.DelegateUserId,
            });

        var after = ordered.Select(s => new { s.StepNumber, s.RoleId, s.SlaHours, s.BackupRoleId, s.PrimaryApproverUserId, s.DelegateUserId }).ToList();
        _audit.Log(PA.PayrollApprovalStepConfigUpdated, PA.ResourceType.PayrollApprovalStepConfig,
            _tenantContext.TenantId.ToString(), before: before, after: after, ipAddress: ipAddress);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payroll approval step-config replaced ({StepCount} steps) by {User} in tenant {TenantId}.",
            ordered.Count, _currentUser.UserId, _tenantContext.TenantId);

        return await GetApprovalStepConfigAsync(cancellationToken);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// BR-5: counts the tenant's eligible payroll approvers — distinct ACTIVE tenant users whose assigned
    /// roles grant the <c>Payroll.Approve</c> permission. When this is &lt; 2 the maker-checker rule is relaxed
    /// (a single HR/Finance user may approve their own submission, the small-team exception). UserTenant /
    /// role join tables are not BaseEntity, so they are filtered explicitly by tenant id.
    /// </summary>
    private async Task<int> CountEligibleApproversAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var query =
            from ut in _dbContext.UserTenants.AsNoTracking()
            where ut.TenantId == tenantId && ut.Status == UserTenantStatus.Active
            join utr in _dbContext.UserTenantRoles.AsNoTracking() on ut.Id equals utr.UserTenantId
            join rp in _dbContext.RolePermissions.AsNoTracking() on utr.RoleId equals rp.RoleId
            where rp.Permission == PermissionCatalog.Payroll.Approve
            select ut.UserId;

        return await query.Distinct().CountAsync(cancellationToken);
    }

    /// <summary>
    /// BUG-076 FR-2: does the acting user hold the given tenant <paramref name="roleId"/>? Uses the same
    /// UserTenant → UserTenantRole join as <see cref="CountEligibleApproversAsync"/>, keyed on the acting user +
    /// the configured step role. UserTenant / role join tables are not BaseEntity, so they are filtered
    /// explicitly by tenant id.
    /// </summary>
    private async Task<bool> UserHoldsRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var userId = _currentUser.UserId;

        return await (
            from ut in _dbContext.UserTenants.AsNoTracking()
            where ut.TenantId == tenantId && ut.UserId == userId && ut.Status == UserTenantStatus.Active
            join utr in _dbContext.UserTenantRoles.AsNoTracking() on ut.Id equals utr.UserTenantId
            where utr.RoleId == roleId
            select ut.Id
        ).AnyAsync(cancellationToken);
    }

    /// <summary>
    /// BUG-076 AC-4 (separation of duties): has the acting user already recorded an Approved decision for this
    /// workflow instance? Shared by <see cref="ApproveAsync"/> (the enforcing check) and
    /// <see cref="GetPendingApprovalsAsync"/> (the queue's distinct-approver filter) so the two can never drift.
    /// </summary>
    private Task<bool> HasCallerAlreadyApprovedAsync(Guid workflowInstanceId, CancellationToken cancellationToken) =>
        _dbContext.PayrollApprovalHistories.AsNoTracking().AnyAsync(
            h => h.WorkflowInstanceId == workflowInstanceId
                && h.Action == PayrollApprovalAction.Approved
                && h.ActorUserId == _currentUser.UserId,
            cancellationToken);

    /// <summary>
    /// US-PAY-008 FR-3 (ISSUE-173): the SLA due-at for the given 1-based approval step — <c>now + SlaHours</c> read
    /// from that step's <see cref="PayrollApprovalStepConfig"/>, or null when the step has no config row or no
    /// SlaHours (opt-in; a step with no SLA never escalates). <c>now</c> is read from the injected clock so the
    /// stamping is deterministically testable.
    /// </summary>
    private async Task<DateTime?> ComputeSlaDueAtAsync(int stepNumber, CancellationToken cancellationToken)
    {
        var slaHours = await _dbContext.PayrollApprovalStepConfigs.AsNoTracking()
            .Where(c => c.StepNumber == stepNumber)
            .Select(c => c.SlaHours)
            .FirstOrDefaultAsync(cancellationToken);

        return slaHours is > 0
            ? _timeProvider.GetUtcNow().UtcDateTime.AddHours(slaHours.Value)
            : null;
    }

    /// <summary>
    /// US-PAY-008 FR-6 (ISSUE-173): evaluate step delegation ONCE at the activation of <paramref name="stepNumber"/>
    /// (submit → step 1; each approve-advance → step N). Delegation is configured only when the step's config sets BOTH
    /// <see cref="PayrollApprovalStepConfig.PrimaryApproverUserId"/> and <see cref="PayrollApprovalStepConfig.DelegateUserId"/>
    /// (partial config is rejected at write time). When it is, AND the primary approver is on an approved leave spanning
    /// today (<see cref="HasActiveApprovedLeaveOnDateAsync"/>, injected clock), the delegate becomes the effective
    /// approver: <see cref="PayrollRun.DelegatedToUserId"/> is stamped and a system-actor <c>Delegated</c>
    /// <see cref="PayrollApprovalHistory"/> row is added to the caller's pending SaveChanges. Otherwise the effective
    /// delegate is cleared. This is a NOTIFICATION/RECORD overlay, NOT an authz change — approval stays role-gated by the
    /// <see cref="ApproveAsync"/> step-role guard (mirrors the generic engine's notify-don't-reassign delegation).
    /// Returns true iff delegation fired, so the caller can dispatch the delegation notification AFTER persistence (the
    /// recipient is resolved from the just-saved <see cref="PayrollRun.DelegatedToUserId"/>).
    /// </summary>
    private async Task<bool> ApplyDelegationAsync(PayrollRun run, int stepNumber, CancellationToken cancellationToken)
    {
        var config = await _dbContext.PayrollApprovalStepConfigs.AsNoTracking()
            .Where(c => c.StepNumber == stepNumber)
            .Select(c => new { c.PrimaryApproverUserId, c.DelegateUserId })
            .FirstOrDefaultAsync(cancellationToken);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        if (config is { PrimaryApproverUserId: { } primary, DelegateUserId: { } delegateUser }
            && await HasActiveApprovedLeaveOnDateAsync(primary, today, cancellationToken))
        {
            run.DelegatedToUserId = delegateUser;
            _dbContext.PayrollApprovalHistories.Add(new PayrollApprovalHistory
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                PayrollRunId = run.Id,
                WorkflowInstanceId = run.CurrentWorkflowInstanceId ?? BaseEntity.NewUuidV7(),
                StepNumber = stepNumber,
                Action = PayrollApprovalAction.Delegated,
                ActorUserId = Guid.Empty,  // system: delegation is applied by the engine, not an acting user.
                Comments = $"Step {stepNumber} primary approver on leave; delegated to {delegateUser}.",
                ActedAt = _timeProvider.GetUtcNow().UtcDateTime,
                IpAddress = null,
            });
            return true;
        }

        run.DelegatedToUserId = null;
        return false;
    }

    /// <summary>
    /// US-PAY-008 FR-6 (ISSUE-173): true when <paramref name="userId"/> maps to a tenant employee who has an APPROVED
    /// leave request spanning <paramref name="onDate"/> — the delegation trigger at step activation. Replicates the
    /// generic engine's leave overlap query (resolve the user's Employee, then any Approved LeaveRequest covering the
    /// date). A user with no linked employee returns false. Tenant-scoped by the global query filter (BR-8).
    /// </summary>
    private async Task<bool> HasActiveApprovedLeaveOnDateAsync(Guid userId, DateOnly onDate, CancellationToken cancellationToken)
    {
        var empId = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.UserId == userId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);
        if (empId is not { } eid)
            return false;

        return await _dbContext.LeaveRequests.AsNoTracking().AnyAsync(
            lr => lr.EmployeeId == eid && lr.Status == LeaveRequestStatus.Approved
                  && lr.StartDate <= onDate && lr.EndDate >= onDate,
            cancellationToken);
    }

    private async Task<(PayrollRun? Run, Result<PayrollApprovalResultDto>? Failure)> LoadRunAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return (null, Result<PayrollApprovalResultDto>.Failure("Tenant context is not resolved.", 400));

        // Tracked load (we mutate Status). Tenant-scoped by the global filter — a cross-tenant run is invisible
        // (returns 404, never leaks cross-tenant existence, BR-8).
        var run = await _dbContext.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        return run is null
            ? (null, Result<PayrollApprovalResultDto>.Failure("Payroll run not found.", 404, "run_not_found"))
            : (run, null);
    }

    private static Result<PayrollApprovalResultDto> InvalidTransition(PayrollRunStatus current, string action) =>
        Result<PayrollApprovalResultDto>.Failure(
            $"A payroll run in status {current} cannot be {action}.", 409, "invalid_transition");

    private PayrollApprovalHistory NewHistory(
        PayrollRun run, Guid instanceId, int step, string action, string? comments, string? ipAddress) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        PayrollRunId = run.Id,
        WorkflowInstanceId = instanceId,
        StepNumber = step,
        Action = action,
        ActorUserId = _currentUser.UserId,
        Comments = comments,
        ActedAt = DateTime.UtcNow,
        IpAddress = ipAddress,
    };

    private static Result<PayrollApprovalResultDto> Ok(PayrollRun run, string action) =>
        Result<PayrollApprovalResultDto>.Success(new PayrollApprovalResultDto
        {
            RunId = run.Id,
            Status = run.Status.ToString(),
            Action = action,
            WorkflowInstanceId = run.CurrentWorkflowInstanceId,
            CurrentApprovalStep = run.CurrentApprovalStep,
            TotalApprovalSteps = run.TotalApprovalSteps,
        });

    private void Log(PayrollRun run, string action) =>
        _logger.LogInformation(
            "Payroll run {RunId} approval action {Action} by {User} in tenant {TenantId}. New status {Status}.",
            run.Id, action, _currentUser.UserId, _tenantContext.TenantId, run.Status);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
