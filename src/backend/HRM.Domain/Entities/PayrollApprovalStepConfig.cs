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
}
