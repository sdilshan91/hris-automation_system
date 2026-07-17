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
}

/// <summary>One (StepNumber, RoleId) pair in a step-config replacement request (US-PAY-008 AC-4/FR-2).</summary>
public sealed record PayrollApprovalStepConfigItem
{
    public int StepNumber { get; init; }
    public Guid RoleId { get; init; }
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
