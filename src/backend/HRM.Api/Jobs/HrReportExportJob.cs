using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Services;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-RPT-004 (FR-5/FR-8): Hangfire background job that generates a large (&ge; 1000-row) report export.
///
/// Enqueued by <c>HrReportExportService.InitiateAsync</c> via the <see cref="IHrReportExportJobScheduler"/> seam.
/// It restores the tenant context into its own scope (so the EF global query filter applies), then delegates the
/// whole regenerate → render → store → notify lifecycle to <see cref="IHrReportExportService.GenerateAsync"/>
/// (which also flips the row to Processing/Completed/Failed). Mirrors the US-LV-012 LeaveReportExportJob +
/// US-ADM-010 DataExportGenerationJob structure.
/// </summary>
public sealed class HrReportExportJob : IHrReportExportJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public HrReportExportJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync(Guid tenantId, Guid exportId, CancellationToken cancellationToken = default)
    {
        Log.Information("HrReportExportJob starting: export {ExportId} for tenant {TenantId}.", exportId, tenantId);

        using var scope = _scopeFactory.CreateScope();

        // Restore the tenant context so the global query filter scopes the export to this tenant (AC-5).
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is TenantContext mutableContext)
            mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);

        var service = scope.ServiceProvider.GetRequiredService<IHrReportExportService>();
        var result = await service.GenerateAsync(exportId, cancellationToken);

        if (result.IsFailure)
            Log.Warning("HrReportExportJob {ExportId} finished with failure: {Error}", exportId, result.Error);
        else
            Log.Information("HrReportExportJob {ExportId} complete.", exportId);
    }
}
