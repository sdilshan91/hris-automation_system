namespace HRM.Application.Common.Interfaces;

/// <summary>
/// BUG-118: enqueues a tenant-scoped background recalculation of leave entitlements after an entitlement rule
/// is edited, so affected employees' already-credited balances are adjusted to the new rule (US-LV-002 AC-5).
/// The Hangfire implementation lives in HRM.Api (keeps the job framework out of Application/Infrastructure);
/// the seam is injected optionally so unit tests and non-Hangfire hosts run without it.
/// </summary>
public interface ILeaveEntitlementRecalcJobScheduler
{
    /// <summary>
    /// Enqueues a recalculation for one tenant, leave year, and (optionally) a single leave type.
    /// Returns the Hangfire job id.
    /// </summary>
    string Enqueue(Guid tenantId, string tenantSubdomain, int leaveYear, Guid? leaveTypeId);
}
