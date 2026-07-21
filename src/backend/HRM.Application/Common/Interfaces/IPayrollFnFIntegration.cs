namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ONB-005 FR-6 / BR-4 seam: triggers the Full &amp; Final (F&amp;F) settlement in Payroll when an offboarding
/// completes. Offboarding only TRIGGERS the settlement; the F&amp;F calculation itself is owned by the Payroll
/// module (BR-4). Implemented by <c>RealPayrollFnFIntegration</c> (ISSUE-294 Phase 1): a tenant-configurable,
/// effective-dated engine that computes + persists a <c>FinalSettlement</c> (idempotent on the offboarding
/// instance) reusing the run's pro-ration, the statutory resolver, and the forfeitable-leave encashment calc.
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
        DateOnly lastWorkingDay,
        CancellationToken cancellationToken = default);
}
