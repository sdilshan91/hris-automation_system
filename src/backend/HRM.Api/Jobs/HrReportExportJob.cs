using HRM.Application.Common.Interfaces;
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

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var service = scope.ServiceProvider.GetRequiredService<IHrReportExportService>();

        // RLS increment 2c: run the export via the shared runner so it sets the tenant context (and, gated on
        // Rls:Enabled, the app.current_tenant GUC) — this export-by-id job stays inside the RLS backstop (AC-5).
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async ct =>
        {
            var result = await service.GenerateAsync(exportId, ct);

            if (result.IsFailure)
                Log.Warning("HrReportExportJob {ExportId} finished with failure: {Error}", exportId, result.Error);
            else
                Log.Information("HrReportExportJob {ExportId} complete.", exportId);
        }, cancellationToken);
    }
}
