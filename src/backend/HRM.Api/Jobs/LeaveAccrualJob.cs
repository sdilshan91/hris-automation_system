using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that processes annual leave entitlement accruals
/// for all tenants (US-LV-002 AC-5, FR-5, NFR-1).
///
/// Runs daily at midnight UTC. For each tenant with active leave types,
/// resolves entitlements and creates accrual ledger entries.
///
/// Batch-processes up to 5,000 employees per tenant per leave type.
/// </summary>
public sealed class LeaveAccrualJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public LeaveAccrualJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting LeaveAccrualJob");

        var leaveYear = DateTime.UtcNow.Year;

        // Get all active tenant IDs.
        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        Log.Information("LeaveAccrualJob: Processing {TenantCount} tenants for year {Year}",
            tenantIds.Count, leaveYear);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                // Set tenant context for this scope.
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
                {
                    mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
                }

                var entitlementService = scope.ServiceProvider.GetRequiredService<ILeaveEntitlementService>();
                await entitlementService.ProcessAccrualsAsync(leaveYear);

                Log.Information("LeaveAccrualJob: Completed accruals for tenant {TenantId}", tenantId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LeaveAccrualJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed LeaveAccrualJob");
    }
}
