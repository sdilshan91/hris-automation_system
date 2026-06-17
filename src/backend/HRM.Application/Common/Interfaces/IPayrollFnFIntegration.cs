namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ONB-005 FR-6 / BR-4 seam: triggers the Full &amp; Final (F&amp;F) settlement workflow in Payroll when an
/// offboarding completes. Offboarding only TRIGGERS the settlement; the F&amp;F calculation itself is owned by
/// the Payroll module (BR-4). The default implementation emits a structured log event (mirrors the
/// performance/payroll LogOnly notification seam) in lieu of a real cross-module call/queue, which does not
/// exist yet. TODO(payroll integration): replace with a real F&amp;F initiation call/event once Payroll exposes
/// an inbound F&amp;F endpoint.
/// </summary>
public interface IPayrollFnFIntegration
{
    /// <summary>
    /// Triggers F&amp;F settlement for a departing employee on offboarding completion (FR-6). Returns a stable
    /// reference id for the triggered settlement (e.g. for audit/correlation).
    /// </summary>
    Task<Guid> TriggerFinalSettlementAsync(
        Guid tenantId,
        Guid employeeId,
        Guid offboardingInstanceId,
        DateTime lastWorkingDay,
        CancellationToken cancellationToken = default);
}
