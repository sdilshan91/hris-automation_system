using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-RPT-004 (BR-3): daily Hangfire recurring job that expires report exports past their 7-day download window
/// and deletes their files. Runs in the system/admin context (cross-tenant). Thin wrapper — the InMemory-testable
/// core lives in <see cref="IHrReportExportCleanupService"/>. Mirrors the US-ADM-010 ExportCleanupJob.
/// </summary>
public sealed class HrReportExportCleanupJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public HrReportExportCleanupJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync()
    {
        using var scope = _scopeFactory.CreateScope();

        // System/admin context so reads/writes span all tenants (cleanup re-scopes per row by tenant id).
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
            mutableContext.SetSystemContext();

        var service = scope.ServiceProvider.GetRequiredService<IHrReportExportCleanupService>();
        var result = await service.ExpireOverdueExportsAsync();

        if (result.IsFailure)
            Log.Warning("HrReportExportCleanupJob: returned '{Error}'.", result.Error);
        else
            Log.Information("HrReportExportCleanupJob: expired {Count} overdue export(s).", result.Value);
    }
}
