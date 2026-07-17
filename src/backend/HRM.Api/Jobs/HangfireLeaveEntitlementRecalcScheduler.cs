using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// BUG-118: Hangfire-backed <see cref="ILeaveEntitlementRecalcJobScheduler"/>. Enqueues the tenant-aware
/// <see cref="RecalcLeaveEntitlementsJob"/> via <c>IBackgroundJobClient.Enqueue</c>. Lives in HRM.Api alongside
/// the job + Hangfire JobStorage; bound to the interface so the Infrastructure LeaveEntitlementService can
/// enqueue by interface (mirrors HangfirePayrollRunJobScheduler). When unregistered (tests/dev without
/// Hangfire storage) the entitlement service simply skips the enqueue.
/// </summary>
public sealed class HangfireLeaveEntitlementRecalcScheduler : ILeaveEntitlementRecalcJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfireLeaveEntitlementRecalcScheduler(IBackgroundJobClient backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public string Enqueue(Guid tenantId, string tenantSubdomain, int leaveYear, Guid? leaveTypeId)
        => _backgroundJobs.Enqueue<RecalcLeaveEntitlementsJob>(
            job => job.RunAsync(tenantId, tenantSubdomain, leaveYear, leaveTypeId));
}
