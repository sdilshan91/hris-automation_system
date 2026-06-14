using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that auto-generates next-year entries for recurring holidays
/// (US-LV-007 FR-3, BR-5).
///
/// Scheduled to run 30 days before year-end (1 December). For each active tenant it asks the
/// holiday service to generate next-year entries for every is_recurring=true holiday. The
/// generation is idempotent: it skips a holiday when next year's entry already exists, so the
/// job is safe to re-run.
/// </summary>
public sealed class HolidayRecurrenceJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public HolidayRecurrenceJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting HolidayRecurrenceJob");

        int targetYear = DateTime.UtcNow.Year + 1;

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        Log.Information("HolidayRecurrenceJob: Processing {TenantCount} tenants for year {Year}",
            tenantIds.Count, targetYear);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                // Restore the tenant context for this scope so global query filters apply.
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
                {
                    mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
                }

                var holidayService = scope.ServiceProvider.GetRequiredService<IHolidayService>();
                var result = await holidayService.GenerateRecurringForYearAsync(tenantId, targetYear);

                Log.Information("HolidayRecurrenceJob: Generated {Count} holidays for tenant {TenantId}",
                    result.IsSuccess ? result.Value : 0, tenantId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HolidayRecurrenceJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed HolidayRecurrenceJob");
    }
}
