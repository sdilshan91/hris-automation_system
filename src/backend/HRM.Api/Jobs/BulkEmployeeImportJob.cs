using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire background job that processes a queued bulk employee import (US-CHR-010 FR-7, AC-4).
/// Sets the tenant context before invoking the service so tenant-scoped queries work.
/// </summary>
public sealed class BulkEmployeeImportJob
{
    private readonly IBulkEmployeeImportService _importService;
    private readonly ITenantContext _tenantContext;

    public BulkEmployeeImportJob(
        IBulkEmployeeImportService importService,
        ITenantContext tenantContext)
    {
        _importService = importService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Invoked by Hangfire. Must restore the tenant context since background jobs
    /// do not pass through the TenantResolutionMiddleware.
    /// </summary>
    public async Task RunAsync(Guid jobId, Guid tenantId, string tenantSubdomain)
    {
        Log.Information("Starting BulkEmployeeImportJob. JobId={JobId}, TenantId={TenantId}", jobId, tenantId);

        // Restore tenant context for the background job scope
        _tenantContext.SetTenant(tenantId, tenantSubdomain, TenantStatus.Active);

        await _importService.ProcessImportJobAsync(jobId, CancellationToken.None);

        Log.Information("Completed BulkEmployeeImportJob. JobId={JobId}", jobId);
    }
}
