using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// BUG-118 (US-LV-002 AC-5): Hangfire background job that recalculates already-accrued employees' leave
/// balances after an entitlement rule is edited. Restores the tenant context from the job arguments (the
/// worker does NOT pass through TenantResolutionMiddleware) via the shared <see cref="ITenantJobRunner"/> so
/// the EF global query filter scopes every query to the rule's tenant, then delegates to
/// <see cref="ILeaveEntitlementService.RecalculateEntitlementsAsync"/>. Idempotent + safely re-runnable.
/// </summary>
public sealed class RecalcLeaveEntitlementsJob
{
    private readonly ILeaveEntitlementService _entitlementService;
    private readonly ITenantJobRunner _jobRunner;

    public RecalcLeaveEntitlementsJob(
        ILeaveEntitlementService entitlementService, ITenantJobRunner jobRunner)
    {
        _entitlementService = entitlementService;
        _jobRunner = jobRunner;
    }

    /// <summary>Invoked by Hangfire with the affected tenant, leave year, and (optionally) a single leave type.</summary>
    public async Task RunAsync(Guid tenantId, string tenantSubdomain, int leaveYear, Guid? leaveTypeId)
    {
        Log.Information(
            "Starting RecalcLeaveEntitlementsJob. TenantId={TenantId}, LeaveYear={LeaveYear}, LeaveTypeId={LeaveTypeId}",
            tenantId, leaveYear, leaveTypeId);

        await _jobRunner.RunForTenantAsync(tenantId, tenantSubdomain, async ct =>
        {
            await _entitlementService.RecalculateEntitlementsAsync(leaveYear, leaveTypeId, ct);
            Log.Information("Completed RecalcLeaveEntitlementsJob. TenantId={TenantId}, LeaveYear={LeaveYear}", tenantId, leaveYear);
        });
    }
}
