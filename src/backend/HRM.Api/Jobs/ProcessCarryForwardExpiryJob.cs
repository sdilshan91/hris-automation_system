using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that expires carried-forward leave days that remain unconsumed past
/// their expiry date (US-LV-008 FR-3, AC-3, BR-3, BR-4).
///
/// Runs monthly. For each active tenant it sets the tenant context and expires any Active
/// carry-forward tracking row whose expiry date has passed, creating an Expired ledger entry for
/// the FIFO-remaining carried days. Idempotent: a tracking row is moved to a terminal state once
/// processed, so it is never double-expired. Mirrors LeaveAccrualJob / HolidayRecurrenceJob.
/// </summary>
public sealed class ProcessCarryForwardExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProcessCarryForwardExpiryJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ProcessCarryForwardExpiryJob");

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        Log.Information("ProcessCarryForwardExpiryJob: Processing {TenantCount} tenants as of {AsOf}",
            tenantIds.Count, asOf);

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
                var result = await service.ProcessExpiryAsync(asOf);

                Log.Information(
                    "ProcessCarryForwardExpiryJob: Tenant {TenantId} expired {Count} tracking rows",
                    tenantId, result.IsSuccess ? result.Value : 0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ProcessCarryForwardExpiryJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed ProcessCarryForwardExpiryJob");
    }
}
