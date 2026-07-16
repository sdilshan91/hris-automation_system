using HRM.Application.Common.Interfaces;
using HRM.Domain.Leave;
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

        // ISSUE-305: `DateTime.UtcNow.Year` is only the right leave year for a CALENDAR tenant. An Apr-Mar
        // tenant's leave year is labelled by the year it STARTS in, so every January-to-March run accrued into
        // the WRONG year for them. The label is now resolved PER TENANT from their FiscalYearStartMonth
        // (default 1 = calendar → byte-identical to the previous behaviour).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Get all active tenants WITH their leave-year start month (one query — no per-tenant lookup).
        List<(Guid Id, int FiscalYearStartMonth)> tenants;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenants = (await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => new { t.Id, t.FiscalYearStartMonth })
                .ToListAsync())
                .Select(t => (t.Id, t.FiscalYearStartMonth))
                .ToList();
        }

        Log.Information("LeaveAccrualJob: Processing {TenantCount} tenants (leave year resolved per tenant)",
            tenants.Count);

        foreach (var (tenantId, fiscalYearStartMonth) in tenants)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
                var entitlementService = scope.ServiceProvider.GetRequiredService<ILeaveEntitlementService>();

                // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant context
                // (and, gated on Rls:Enabled, the app.current_tenant GUC) — keeping it inside the RLS backstop.
                await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}",
                    async _ => await entitlementService.ProcessAccrualsAsync(
                        LeaveYear.LabelFor(today, fiscalYearStartMonth)));

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
