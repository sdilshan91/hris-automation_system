using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// ISSUE-178 PR2: Hangfire background job that generates a large (&ge; 1000-row) payroll-report export.
///
/// Enqueued by <c>PayrollReportExportService.InitiateAsync</c> via the <see cref="IPayrollReportExportJobScheduler"/>
/// seam. It restores the tenant context into its own scope (so the EF global query filter applies), then delegates
/// the whole regenerate → render → store → notify lifecycle to <see cref="IPayrollReportExportService.GenerateAsync"/>
/// (which also flips the row to Processing/Completed/Failed). A 1:1 clone of <see cref="HrReportExportJob"/>.
/// </summary>
public sealed class PayrollReportExportJob : IPayrollReportExportJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PayrollReportExportJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync(Guid tenantId, Guid exportId, CancellationToken cancellationToken = default)
    {
        Log.Information("PayrollReportExportJob starting: export {ExportId} for tenant {TenantId}.", exportId, tenantId);

        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var service = scope.ServiceProvider.GetRequiredService<IPayrollReportExportService>();

        // Run the export via the shared runner so it sets the tenant context (and, gated on Rls:Enabled, the
        // app.current_tenant GUC) — this export-by-id job stays inside the RLS backstop (AC-5).
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async ct =>
        {
            var result = await service.GenerateAsync(exportId, ct);

            if (result.IsFailure)
                Log.Warning("PayrollReportExportJob {ExportId} finished with failure: {Error}", exportId, result.Error);
            else
                Log.Information("PayrollReportExportJob {ExportId} complete.", exportId);
        }, cancellationToken);
    }
}
