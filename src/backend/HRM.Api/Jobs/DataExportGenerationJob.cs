using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ADM-010 (AC-1/AC-2): Hangfire job that generates a tenant export bundle. TENANT-AWARE — the export id is
/// passed in the job args; the job looks up the request's tenant and restores the tenant context for its scope so
/// the EF global query filters scope reads (mirrors <see cref="TenantDeletionJob"/>). The heavy work lives in
/// <see cref="ITenantDataExportService.GenerateAsync"/>, which is also directly callable from tests without
/// Hangfire.
/// </summary>
public sealed class DataExportGenerationJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DataExportGenerationJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync(Guid exportRequestId)
    {
        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var export = await db.ExportRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == exportRequestId);
        if (export is null)
        {
            Log.Warning("DataExportGenerationJob: export {ExportId} not found — skipping.", exportRequestId);
            return;
        }

        // Restore tenant context so the generation reads are scoped to the target tenant.
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
            mutableContext.SetTenant(export.TenantId, $"tenant-{export.TenantId}", TenantStatus.Active);

        var service = scope.ServiceProvider.GetRequiredService<ITenantDataExportService>();
        var result = await service.GenerateAsync(exportRequestId);

        if (result.IsFailure)
            Log.Warning("DataExportGenerationJob: export {ExportId} returned '{Error}' ({Code}).",
                exportRequestId, result.Error, result.ErrorCode);
        else
            Log.Information("DataExportGenerationJob: export {ExportId} completed.", exportRequestId);
    }
}
