namespace HRM.Domain.Entities;

/// <summary>
/// An immutable audit record of an action taken in a payroll-run approval workflow (US-PAY-008 FR-7/NFR-5).
/// One row is written per action (submit / approve / reject / return / escalate). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>. Mirrors the
/// established <see cref="LeaveApprovalHistory"/> / <see cref="RegularizationApprovalHistory"/> pattern —
/// there is no shared approval-workflow engine yet (technical doc section 34 / US-ADM-007 is not built), so
/// this is a payroll-specific approval flow. Maps to the "payroll_approval_history" table.
///
/// <para>NFR-5 (immutable audit): rows are only ever inserted, never updated or deleted — there is no
/// update/delete path in the approval service. The AuditInterceptor additionally stamps created_at /
/// created_by on insert.</para>
///
/// <para><see cref="WorkflowInstanceId"/> groups the rows of a single submit-to-decision attempt. A rejected
/// run that is re-submitted (BR-3) starts a NEW workflow instance (new id), so the history preserves every
/// attempt. <see cref="StepNumber"/> is the 1-based approval step within that instance (multi-step routing,
/// AC-4); single-step is always step 1.</para>
/// </summary>
public sealed class PayrollApprovalHistory : BaseEntity
{
    /// <summary>FK to the <see cref="PayrollRun"/> this action applies to (required).</summary>
    public Guid PayrollRunId { get; set; }

    /// <summary>
    /// Groups all actions of one submit-to-decision attempt (US-PAY-008 §7). A re-submit after rejection
    /// (BR-3) begins a new instance id.
    /// </summary>
    public Guid WorkflowInstanceId { get; set; }

    /// <summary>1-based approval step within the workflow instance (AC-4). Always 1 for single-step.</summary>
    public int StepNumber { get; set; } = 1;

    /// <summary>
    /// The action taken: one of <see cref="PayrollApprovalAction"/> (Submitted, Approved, Rejected, Returned,
    /// Escalated). Stored as varchar(20).
    /// </summary>
    public string Action { get; set; } = default!;

    /// <summary>User (UserTenant/user id) who took the action (FR-7, required).</summary>
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Optional comment; mandatory reason on Rejected (AC-3) and Returned (FR-9).
    /// </summary>
    public string? Comments { get; set; }

    /// <summary>When the action was taken (FR-7).</summary>
    public DateTime ActedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Originating IP address of the actor (FR-7), captured from the request when available; null otherwise
    /// (e.g. background/job context). varchar(50).
    /// </summary>
    public string? IpAddress { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public PayrollRun? PayrollRun { get; set; }
}

/// <summary>
/// Allowed values for <see cref="PayrollApprovalHistory.Action"/> (US-PAY-008 §7). String constants (not an
/// enum) so the persisted value == the data-spec literal.
/// </summary>
public static class PayrollApprovalAction
{
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Returned = "Returned";
    public const string Escalated = "Escalated";
}
