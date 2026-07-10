namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-011b (AC-5/FR-8/BR-4/NFR-3): escalates approval steps whose SLA has elapsed. Runs in a resolved-tenant
/// context (the Hangfire job sets it per tenant via <c>ITenantJobRunner</c>). Idempotent — each breached step is
/// escalated at most once (a conditional compare-and-swap on the step row guarantees at-most-once even across
/// overlapping job runs).
/// </summary>
public interface IWorkflowSlaEscalator
{
    /// <summary>
    /// Escalate every Pending step-instance in the CURRENT tenant context whose SLA has elapsed and that has not
    /// yet been escalated. Idempotent. Returns the count of steps escalated in this pass.
    /// </summary>
    Task<int> EscalateDueStepsAsync(CancellationToken cancellationToken = default);
}
