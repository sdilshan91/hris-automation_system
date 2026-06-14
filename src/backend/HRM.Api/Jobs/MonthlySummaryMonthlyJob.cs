using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ATT-007 FR-2: monthly recurring job that runs on the 1st of each month to finalize and cache the
/// previous month's attendance summary, per tenant. Restores the tenant context per tenant (so the EF
/// global query filters apply) and calls the summary service for the just-closed month.
///
/// TENANT TIMEZONE: anchors on the UTC "previous month" — no tenant-timezone infra yet (same deferral
/// as AutoClockOutJob, US-ATT-002). Idempotent and tenant-safe; mirrors <see cref="AutoClockOutJob"/>.
/// </summary>
public sealed class MonthlySummaryMonthlyJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MonthlySummaryMonthlyJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting MonthlySummaryMonthlyJob");

        // The previous calendar month (UTC).
        var firstOfThisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var prevMonth = firstOfThisMonth.AddMonths(-1);
        int year = prevMonth.Year, month = prevMonth.Month;

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
                using var scope = _scopeFactory.CreateScope();
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenantContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);

                var service = scope.ServiceProvider.GetRequiredService<IAttendanceSummaryService>();
                var result = await service.GenerateAsync(year, month, CancellationToken.None);
                if (result.IsFailure)
                    Log.Warning("MonthlySummaryMonthlyJob: tenant {TenantId} failed: {Error}",
                        tenantId, result.Error);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MonthlySummaryMonthlyJob: failed for tenant {TenantId}", tenantId);
            }
        }

        Log.Information("Completed MonthlySummaryMonthlyJob for {Year}-{Month}", year, month);
    }
}
