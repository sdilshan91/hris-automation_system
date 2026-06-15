using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire background job that processes a queued payroll run (US-PAY-003 FR-2/FR-3). Restores the tenant
/// context from the job arguments (the worker does NOT pass through TenantResolutionMiddleware) so the EF
/// global query filter scopes every query to the run's tenant (AC-7), then delegates the heavy compute to
/// <see cref="IPayrollRunProcessor"/>. Idempotent + safely re-runnable (NFR-2): re-processing replaces the
/// run's prior slips (FR-7).
/// </summary>
public sealed class ProcessPayrollRunJob
{
    private readonly IPayrollRunProcessor _processor;
    private readonly ITenantContext _tenantContext;

    public ProcessPayrollRunJob(IPayrollRunProcessor processor, ITenantContext tenantContext)
    {
        _processor = processor;
        _tenantContext = tenantContext;
    }

    /// <summary>Invoked by Hangfire with the run's tenant + id (FR-3).</summary>
    public async Task RunAsync(Guid tenantId, string tenantSubdomain, Guid runId)
    {
        Log.Information("Starting ProcessPayrollRunJob. RunId={RunId}, TenantId={TenantId}", runId, tenantId);

        // Restore tenant context for the background-job scope (FR-3) so RLS/global filters apply.
        _tenantContext.SetTenant(tenantId, tenantSubdomain, TenantStatus.Active);

        var result = await _processor.ProcessAsync(runId, CancellationToken.None);

        if (result.IsFailure)
            Log.Warning("ProcessPayrollRunJob did not complete. RunId={RunId}, Error={Error}", runId, result.Error);
        else
            Log.Information("Completed ProcessPayrollRunJob. RunId={RunId}", runId);
    }
}
