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

        // Cross-tenant lookup FIRST: the job args carry only the export id, so we must find the request (and its
        // tenant) with no tenant context yet. This read runs with an unresolved ambient → the connection router
        // picks the privileged (hrm_owner/BYPASSRLS) connection under RLS, so the IgnoreQueryFilters lookup is
        // NOT fail-closed. (Increment 2c: routing is inert until PrivilegedConnection is populated.)
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var export = await db.ExportRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == exportRequestId);
        if (export is null)
        {
            Log.Warning("DataExportGenerationJob: export {ExportId} not found — skipping.", exportRequestId);
            return;
        }

        // Now run the generation for the resolved tenant via the shared runner so it sets the tenant context
        // (and, gated on Rls:Enabled, the app.current_tenant GUC) — the generation reads stay inside the RLS backstop.
        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var service = scope.ServiceProvider.GetRequiredService<ITenantDataExportService>();

        await runner.RunForTenantAsync(export.TenantId, $"tenant-{export.TenantId}", async _ =>
        {
            var result = await service.GenerateAsync(exportRequestId);

            if (result.IsFailure)
                Log.Warning("DataExportGenerationJob: export {ExportId} returned '{Error}' ({Code}).",
                    exportRequestId, result.Error, result.ErrorCode);
            else
                Log.Information("DataExportGenerationJob: export {ExportId} completed.", exportRequestId);
        });
    }
}
