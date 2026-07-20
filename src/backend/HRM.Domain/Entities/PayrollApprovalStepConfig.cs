namespace HRM.Domain.Entities;

/// <summary>
/// US-PAY-008 AC-4 / FR-2 (BUG-076): a tenant's configurable payroll-approval step → role binding. One row
/// per (tenant, step) declares WHICH tenant <see cref="Role"/> is authorised to approve that 1-based step of
/// the payroll-run approval workflow. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global
/// query filter + <c>TenantInterceptor</c>. Maps to the "payroll_approval_step_config" table.
///
/// <para>This is a PAYROLL-SPECIFIC config surface — deliberately NOT the shared Admin WorkflowRuntime
/// (US-ADM-007), which is a separate engine. When a tenant has &gt;= 1 row the configured step COUNT is the
/// authoritative <see cref="PayrollRun.TotalApprovalSteps"/> and each step's <see cref="RoleId"/> gates who
/// may approve it (the step-role check in <c>PayrollApprovalService.ApproveAsync</c>). A tenant with no rows
/// keeps the legacy caller-supplied step count (default 1).</para>
/// </summary>
public sealed class PayrollApprovalStepConfig : BaseEntity
{
    /// <summary>1-based approval step this row configures (contiguous 1..N across the tenant's rows).</summary>
    public int StepNumber { get; set; }

    /// <summary>
    /// The tenant <see cref="Role"/> authorised to approve <see cref="StepNumber"/>. The role must exist in the
    /// tenant AND hold the <c>Payroll.Approve</c> permission (validated on write) — a step role that cannot pass
    /// the controller's approve gate would be a dead misconfiguration.
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// US-PAY-008 FR-3 (ISSUE-173): per-step approval SLA in hours. Opt-in and nullable — when set (&gt; 0) a run
    /// sitting on this step past <c>SubmittedAt/step-advance + SlaHours</c> is escalated once by the recurring
    /// <c>PayrollApprovalSlaEscalationJob</c>. Null = this step never escalates (no SLA configured).
    /// </summary>
    public int? SlaHours { get; set; }

    /// <summary>
    /// US-PAY-008 FR-3 (ISSUE-173): the tenant <see cref="Role"/> notified when this step breaches its SLA (the
    /// backup approver role). Payroll is role-gated, so escalation NOTIFIES this role's holders (who can act via
    /// the existing pending-approvals queue) rather than hard-reassigning to a user. Like <see cref="RoleId"/> it
    /// must, when set, exist in the tenant AND hold <c>Payroll.Approve</c> (validated on write). Null → the tenant
    /// approver pool is notified as a fallback.
    /// </summary>
    public Guid? BackupRoleId { get; set; }

    /// <summary>
    /// US-PAY-008 FR-6 (ISSUE-173): the step's designated PRIMARY approver user. Delegation is configured on a step
    /// only when BOTH this and <see cref="DelegateUserId"/> are set (a partial config is rejected on write). When set
    /// it must be an ACTIVE in-tenant user holding <c>Payroll.Approve</c>. At step activation, if this user is on an
    /// approved leave spanning today, the <see cref="DelegateUserId"/> becomes the effective approver — recorded on
    /// <see cref="PayrollRun.DelegatedToUserId"/> + notified, NOT an authz change (approval stays role-gated). Null =
    /// no per-step delegation configured.
    /// </summary>
    public Guid? PrimaryApproverUserId { get; set; }

    /// <summary>
    /// US-PAY-008 FR-6 (ISSUE-173): the user the step delegates to when its <see cref="PrimaryApproverUserId"/> is on
    /// leave at activation. Set together with (and validated identically to) the primary approver — an ACTIVE
    /// in-tenant user holding <c>Payroll.Approve</c> so the delegate can actually act. Null = no delegation configured.
    /// </summary>
    public Guid? DelegateUserId { get; set; }
}
