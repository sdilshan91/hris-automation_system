namespace HRM.Application.Features.Payroll.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-PAY-008 — Payroll approval-workflow DTOs. Base: /api/v1/payroll, ApiResponse<T> envelope,
//  PascalCase enum strings (US-PLT-003: PayrollRunStatus values incl. AwaitingApproval, Approved,
//  Rejected, Finalized). Money is decimal numeric(18,2).
// ════════════════════════════════════════════════════════════════════════

/// <summary>Request body carrying an optional comment (submit / approve / return) or required reason (reject).</summary>
public sealed record PayrollApprovalActionRequest
{
    /// <summary>Free-text comment/reason. Required for Reject (AC-3) and Return (FR-9); optional otherwise.</summary>
    public string? Comments { get; init; }
}

/// <summary>Request body for SubmitForApproval (US-PAY-008 AC-1). Optionally configures multi-step routing (AC-4).</summary>
public sealed record SubmitForApprovalRequest
{
    /// <summary>Optional submit note recorded in the approval history.</summary>
    public string? Comments { get; init; }

    /// <summary>
    /// Total sequential approval steps for this submission (AC-4/FR-2). Defaults to 1 (single-step
    /// HR-submit/Finance-approve, BR-2). Values &gt; 1 require each step to be approved before the run is
    /// Approved.
    /// </summary>
    public int? TotalApprovalSteps { get; init; }
}

/// <summary>The outcome of an approval-workflow action (US-PAY-008). Status is the new PayrollRunStatus string.</summary>
public sealed record PayrollApprovalResultDto
{
    public Guid RunId { get; init; }

    /// <summary>New run status string (AwaitingApproval, Approved, Rejected, ReviewPending, Finalized).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The action just recorded (Submitted, Approved, Rejected, Returned, Escalated).</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Active workflow instance id (groups this attempt's history rows). Null after a return/reject clears it.</summary>
    public Guid? WorkflowInstanceId { get; init; }

    /// <summary>Current step awaiting decision within the instance (AC-4); null when not awaiting approval.</summary>
    public int? CurrentApprovalStep { get; init; }

    /// <summary>Total configured steps for the instance (AC-4); null when not in a workflow.</summary>
    public int? TotalApprovalSteps { get; init; }
}

// ── Configurable step → role approval config (US-PAY-008 AC-4/FR-2, BUG-076) ─────────────────

/// <summary>One configured approval step's role binding as returned by GET step-config (US-PAY-008 AC-4/FR-2).</summary>
public sealed record PayrollApprovalStepConfigDto
{
    /// <summary>1-based approval step (contiguous 1..N).</summary>
    public int StepNumber { get; init; }

    /// <summary>The tenant Role authorised to approve this step.</summary>
    public Guid RoleId { get; init; }

    /// <summary>The role's display name (resolved for the caller; empty if the role was since removed).</summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary>US-PAY-008 FR-3 (ISSUE-173): per-step approval SLA in hours; null = no SLA (never escalates).</summary>
    public int? SlaHours { get; init; }

    /// <summary>US-PAY-008 FR-3 (ISSUE-173): the backup approver role notified on SLA breach; null = approver-pool fallback.</summary>
    public Guid? BackupRoleId { get; init; }

    /// <summary>The backup role's display name (resolved for the caller; empty when no backup role / role removed).</summary>
    public string? BackupRoleName { get; init; }

    /// <summary>
    /// US-PAY-008 FR-6 (ISSUE-173): the step's designated primary approver user; null = no delegation configured.
    /// Delegation is active only when this AND <see cref="DelegateUserId"/> are both set.
    /// </summary>
    public Guid? PrimaryApproverUserId { get; init; }

    /// <summary>US-PAY-008 FR-6 (ISSUE-173): the user the step delegates to when the primary approver is on leave; null = none.</summary>
    public Guid? DelegateUserId { get; init; }
}

/// <summary>One (StepNumber, RoleId) pair in a step-config replacement request (US-PAY-008 AC-4/FR-2).</summary>
public sealed record PayrollApprovalStepConfigItem
{
    public int StepNumber { get; init; }
    public Guid RoleId { get; init; }

    /// <summary>US-PAY-008 FR-3 (ISSUE-173): optional per-step SLA in hours; when set must be &gt; 0. Null = no SLA.</summary>
    public int? SlaHours { get; init; }

    /// <summary>
    /// US-PAY-008 FR-3 (ISSUE-173): optional backup approver role notified on SLA breach. When set it must exist in
    /// the tenant AND hold Payroll.Approve (same validation as <see cref="RoleId"/>). Null = approver-pool fallback.
    /// </summary>
    public Guid? BackupRoleId { get; init; }

    /// <summary>
    /// US-PAY-008 FR-6 (ISSUE-173): optional per-step PRIMARY approver user for delegation. When set, <see
    /// cref="DelegateUserId"/> must also be set (partial config → 400 <c>delegation_config_incomplete</c>) and both
    /// must be ACTIVE in-tenant users holding Payroll.Approve. Null = no delegation on this step.
    /// </summary>
    public Guid? PrimaryApproverUserId { get; init; }

    /// <summary>
    /// US-PAY-008 FR-6 (ISSUE-173): optional per-step DELEGATE user, effective when the primary approver is on leave at
    /// activation. Set together with (and validated identically to) <see cref="PrimaryApproverUserId"/>. Null = none.
    /// </summary>
    public Guid? DelegateUserId { get; init; }
}

/// <summary>
/// Request body for PUT step-config (US-PAY-008 AC-4/FR-2, BUG-076): the FULL ordered set of steps that
/// atomically REPLACES the tenant's payroll-approval step → role config. Steps must be contiguous 1..N and
/// every role must exist in the tenant and hold Payroll.Approve.
/// </summary>
public sealed record SetPayrollApprovalStepConfigRequest
{
    public IReadOnlyList<PayrollApprovalStepConfigItem> Steps { get; init; } = [];
}

/// <summary>
/// DF-14: one AwaitingApproval run that the CURRENT caller can approve RIGHT NOW — a row in the approver's
/// "pending approvals" queue (US-PAY-008). The queue mirrors the eligibility predicates in
/// <c>PayrollApprovalService.ApproveAsync</c> exactly: a run appears here iff calling approve on it would not
/// 403 (step-role match, maker-checker, distinct-approver). Field shape matches the FE <c>IPayrollRun</c>
/// fields the queue renders.
/// </summary>
public sealed record PendingApprovalDto
{
    public Guid RunId { get; init; }
    public int PayMonth { get; init; }
    public int PayYear { get; init; }

    /// <summary>Run status string — always <c>AwaitingApproval</c> for this queue.</summary>
    public string Status { get; init; } = string.Empty;

    public int ProcessedEmployees { get; init; }
    public int TotalEmployees { get; init; }
    public decimal TotalGross { get; init; }
    public decimal TotalNet { get; init; }

    /// <summary>The user who submitted the run for approval (maker, BR-5). <see cref="Guid.Empty"/> if unset.</summary>
    public Guid SubmittedBy { get; init; }

    /// <summary>Submitter display name, resolved via a batched keyed Employees/Users lookup (no N+1).</summary>
    public string InitiatedByName { get; init; } = string.Empty;

    /// <summary>When the run was initiated (FR-1).</summary>
    public DateTime InitiatedAt { get; init; }

    /// <summary>Current 1-based step awaiting decision (AC-4); null when not multi-step.</summary>
    public int? CurrentApprovalStep { get; init; }

    /// <summary>Total configured approval steps for the active instance (AC-4).</summary>
    public int? TotalApprovalSteps { get; init; }
}

/// <summary>A single row of a run's approval timeline (US-PAY-008 FR-7, §8 timeline view).</summary>
public sealed record PayrollApprovalHistoryDto
{
    public Guid Id { get; init; }
    public Guid PayrollRunId { get; init; }
    public Guid WorkflowInstanceId { get; init; }
    public int StepNumber { get; init; }
    public string Action { get; init; } = string.Empty;
    public Guid ActorUserId { get; init; }
    public string? Comments { get; init; }
    public DateTime ActedAt { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// The approval-review summary an approver sees (US-PAY-008 FR-4). Reuses the run's stored totals; the
/// previous-month total + variance come from the prior Finalized run for the tenant. Exceptions are computed
/// warnings (missing structures, negative net, skipped employees).
/// </summary>
public sealed record PayrollApprovalSummaryDto
{
    public Guid RunId { get; init; }
    public int PayMonth { get; init; }
    public int PayYear { get; init; }
    public string Status { get; init; } = string.Empty;

    public int TotalEmployees { get; init; }
    public decimal TotalGross { get; init; }
    public decimal TotalDeductions { get; init; }
    public decimal TotalStatutory { get; init; }
    public decimal TotalNet { get; init; }

    /// <summary>Previous month's total net (the prior Finalized run for the tenant); null when none exists.</summary>
    public decimal? PreviousMonthTotalNet { get; init; }

    /// <summary>
    /// Month-over-month change of total net as a percentage; null when there is no previous-month baseline.
    /// </summary>
    public decimal? VariancePercentage { get; init; }

    /// <summary>Warnings/exceptions for the approver to review (missing structures, negative net, skipped).</summary>
    public IReadOnlyList<string> Exceptions { get; init; } = [];
}
