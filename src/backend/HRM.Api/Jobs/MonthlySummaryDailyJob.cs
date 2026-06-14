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

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);

        var service = scope.ServiceProvider.GetRequiredService<IAttendanceSummaryService>();
        var result = await service.GenerateAsync(year, month, CancellationToken.None);

        if (result.IsFailure)
            Log.Warning("MonthlySummaryDailyJob: tenant {TenantId} generation failed: {Error}",
                tenantId, result.Error);
    }
}
