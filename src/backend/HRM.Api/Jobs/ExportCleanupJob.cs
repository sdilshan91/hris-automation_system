using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ADM-010 (FR-7): hourly Hangfire recurring job that expires export bundles past their 72h download window
/// and deletes their files. Runs in the system/admin context (cross-tenant). Thin wrapper — the InMemory-testable
/// core lives in <see cref="IExportCleanupService"/>.
/// </summary>
public sealed class ExportCleanupJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ExportCleanupJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync()
    {
        using var scope = _scopeFactory.CreateScope();

        // System/admin context so reads/writes span all tenants (cleanup re-scopes per row by tenant id).
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
            mutableContext.SetSystemContext();

        var service = scope.ServiceProvider.GetRequiredService<IExportCleanupService>();
        var result = await service.ExpireOverdueExportsAsync();

        if (result.IsFailure)
            Log.Warning("ExportCleanupJob: returned '{Error}'.", result.Error);
        else
            Log.Information("ExportCleanupJob: expired {Count} overdue export(s).", result.Value);
    }
}
