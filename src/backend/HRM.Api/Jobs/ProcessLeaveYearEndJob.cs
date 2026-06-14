using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that processes year-end leave carry-forward and forfeiture for all
/// tenants (US-LV-008 FR-2, AC-1/AC-2/AC-4, BR-1/BR-2/BR-6).
///
/// Scheduled for the first day of the new leave year (1 January). For each active tenant it sets
/// the tenant context and processes the just-closed year: for each employee × applicable leave
/// type it computes the unused balance, carries forward MIN(unused, limit), forfeits (expires or
/// encashes) the remainder, and writes a carry-forward tracking row.
///
/// Idempotent (NFR-3): a re-run skips any pair already tracked for the closing year. The existing
/// Hangfire/Polly setup provides retry. Mirrors LeaveAccrualJob / HolidayRecurrenceJob.
/// </summary>
public sealed class ProcessLeaveYearEndJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProcessLeaveYearEndJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ProcessLeaveYearEndJob");

        // The job runs on 1 January, so the closing leave year is the previous calendar year.
        // TODO(tenant-settings): derive the closing leave year from tenant fiscal-year config.
        int fromYear = DateTime.UtcNow.Year - 1;

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        Log.Information("ProcessLeaveYearEndJob: Processing {TenantCount} tenants for closing year {Year}",
            tenantIds.Count, fromYear);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
                {
                    mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
                }

                var service = scope.ServiceProvider.GetRequiredService<ILeaveCarryForwardService>();
                var result = await service.ProcessYearEndAsync(fromYear);

                Log.Information(
                    "ProcessLeaveYearEndJob: Tenant {TenantId} carried forward {Count} pairs for year {Year}",
                    tenantId, result.IsSuccess ? result.Value : 0, fromYear);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ProcessLeaveYearEndJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed ProcessLeaveYearEndJob");
    }
}
