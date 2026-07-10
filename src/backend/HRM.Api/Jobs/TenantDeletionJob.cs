using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire job that fires at a terminating tenant's <c>TerminationScheduledAt</c> to HARD-DELETE all per-tenant
/// data and mark the tenant <c>Terminated</c> with PII redacted (US-ADM-004 AC-4/FR-3). It is TENANT-AWARE: the
/// tenant id is passed in the job args and the job restores the tenant context for its scope so the EF global
/// query filters scope reads (mirrors <c>OfferExpiryJob</c>). Idempotent: the deletion service no-ops if the
/// tenant is no longer in <c>Terminating</c> status (e.g. a Restore de-queued / superseded it).
/// </summary>
public sealed class TenantDeletionJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantDeletionJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync(Guid tenantId)
    {
        using var scope = _scopeFactory.CreateScope();

        // RLS increment 2c: tenant HARD-DELETE is a privileged platform operation. The deletion service spans the
        // target tenant's rows via IgnoreQueryFilters + explicit tenant-id scoping AND manages its OWN retry-safe
        // transaction — so it routes on the SYSTEM context (privileged BYPASSRLS connection), NOT the per-tenant
        // runner (whose transaction would nest inside the deletion service's own transaction).
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetSystemContext();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stillTerminating = await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Id == tenantId && t.Status == TenantStatus.Terminating);
        if (!stillTerminating)
        {
            Log.Information(
                "TenantDeletionJob: tenant {TenantId} is no longer Terminating — skipping deletion.", tenantId);
            return;
        }

        var deletion = scope.ServiceProvider.GetRequiredService<ITenantDataDeletionService>();
        var result = await deletion.DeleteTenantDataAsync(tenantId);

        if (result.IsFailure)
        {
            Log.Warning(
                "TenantDeletionJob: deletion for tenant {TenantId} returned '{Error}' ({Code}).",
                tenantId, result.Error, result.ErrorCode);
            return;
        }

        Log.Information(
            "TenantDeletionJob: terminated tenant {TenantId} — deleted {Rows} row(s) across {Types} type(s).",
            tenantId, result.Value!.RowsDeleted, result.Value.EntityTypesDeleted);
    }
}
