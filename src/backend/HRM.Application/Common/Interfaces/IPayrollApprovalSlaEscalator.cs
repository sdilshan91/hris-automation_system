namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-PAY-008 FR-3 (ISSUE-173): escalates payroll-run approvals whose current-step SLA has elapsed, in the CURRENT
/// tenant context (the Hangfire <c>PayrollApprovalSlaEscalationJob</c> sets it per tenant via <c>ITenantJobRunner</c>).
/// Idempotent — each breached run is escalated at most once (a conditional compare-and-swap on <c>EscalatedAt</c>
/// guarantees at-most-once even across overlapping job runs).
///
/// <para>Escalation is a NOTIFY, not a hard reassignment: payroll approvals are role-gated, so a breach writes an
/// <c>Escalated</c> history row and notifies the current step's backup approver role (or the tenant approver pool
/// when no backup is configured). Any holder of the step/backup role can then act via the existing pending-approvals
/// queue.</para>
/// </summary>
public interface IPayrollApprovalSlaEscalator
{
    /// <summary>
    /// Escalate every AwaitingApproval run in the CURRENT tenant context whose current-step SLA has elapsed and that
    /// has not yet been escalated. Idempotent. Returns the count of runs escalated in this pass.
    /// </summary>
    Task<int> EscalateDueStepsAsync(CancellationToken cancellationToken = default);
}
