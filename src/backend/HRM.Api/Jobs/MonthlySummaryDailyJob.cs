using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ATT-007 FR-1: daily recurring job that refreshes the attendance monthly summary for the previous
/// day's month, per tenant. Because the summary is materialized per-month, the daily refresh recomputes
/// the (still-incomplete) current month so the HR view stays close to real-time without a per-request
/// recompute. For each active/trial tenant it restores the tenant context (so the EF global query
/// filters apply) and calls the summary service.
///
/// TENANT TIMEZONE: anchors on UTC "yesterday" — no tenant-timezone infra yet (same deferral as
/// AutoClockOutJob, US-ATT-002). Idempotent and tenant-safe; mirrors <see cref="AutoClockOutJob"/>.
/// </summary>
public sealed class MonthlySummaryDailyJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MonthlySummaryDailyJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting MonthlySummaryDailyJob");

        // Refresh the month containing "yesterday" (UTC) — covers an end-of-month roll naturally.
        var target = DateTime.UtcNow.Date.AddDays(-1);
        int year = target.Year, month = target.Month;

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await GenerateForTenantAsync(tenantId, year, month);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MonthlySummaryDailyJob: failed for tenant {TenantId}", tenantId);
            }
        }

        Log.Information("Completed MonthlySummaryDailyJob for {Year}-{Month}", year, month);
    }

    private async Task GenerateForTenantAsync(Guid tenantId, int year, int month)
    {
        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var service = scope.ServiceProvider.GetRequiredService<IAttendanceSummaryService>();

        // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant context
        // (and, gated on Rls:Enabled, the app.current_tenant GUC) — keeping it inside the RLS backstop.
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async ct =>
        {
            var result = await service.GenerateAsync(year, month, ct);
            if (result.IsFailure)
                Log.Warning("MonthlySummaryDailyJob: tenant {TenantId} generation failed: {Error}",
                    tenantId, result.Error);
        });
    }
}
