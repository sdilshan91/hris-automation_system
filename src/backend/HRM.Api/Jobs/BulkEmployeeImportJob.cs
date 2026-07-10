using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire background job that processes a queued bulk employee import (US-CHR-010 FR-7, AC-4).
/// Sets the tenant context (via the shared <see cref="ITenantJobRunner"/>) before invoking the service so
/// tenant-scoped queries work — and, gated on Rls:Enabled, sets the app.current_tenant GUC (RLS increment 2c).
/// </summary>
public sealed class BulkEmployeeImportJob
{
    private readonly IBulkEmployeeImportService _importService;
    private readonly ITenantJobRunner _jobRunner;

    public BulkEmployeeImportJob(
        IBulkEmployeeImportService importService,
        ITenantJobRunner jobRunner)
    {
        _importService = importService;
        _jobRunner = jobRunner;
    }

    /// <summary>
    /// Invoked by Hangfire. Must restore the tenant context since background jobs
    /// do not pass through the TenantResolutionMiddleware.
    /// </summary>
    public async Task RunAsync(Guid jobId, Guid tenantId, string tenantSubdomain)
    {
        Log.Information("Starting BulkEmployeeImportJob. JobId={JobId}, TenantId={TenantId}", jobId, tenantId);

        await _jobRunner.RunForTenantAsync(tenantId, tenantSubdomain,
            ct => _importService.ProcessImportJobAsync(jobId, ct));

        Log.Information("Completed BulkEmployeeImportJob. JobId={JobId}", jobId);
    }
}
