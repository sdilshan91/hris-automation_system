using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Daily Hangfire recurring job for goal-tracking stale-goal detection (US-PRF-009 AC-5/FR-6/BR-4/NFR-5). For each
/// active/trial tenant it sets the tenant context (so the EF global query filters apply) and delegates to
/// <see cref="IStaleGoalNudgeService.RunSweepAsync"/>, which — when the tenant has the sweep enabled
/// (<c>Tenant.StaleGoalNudgeDays</c> &gt; 0, BR-4) — nudges employees whose active goals have gone without a
/// progress update beyond the configured interval and flags those goals "Needs Attention" for the manager
/// dashboard (derived live on the read side). Mirrors the tenant-iteration structure of
/// <see cref="PipReminderJob"/>: idempotent per day and tenant-safe.
/// </summary>
public sealed class StaleGoalNudgeJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public StaleGoalNudgeJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting StaleGoalNudgeJob");

        var now = DateTime.UtcNow;

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
                var sweep = scope.ServiceProvider.GetRequiredService<IStaleGoalNudgeService>();

                // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant context
                // (and, gated on Rls:Enabled, the app.current_tenant GUC) — keeping it inside the RLS backstop.
                await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
                {
                    total += await sweep.RunSweepAsync(now);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "StaleGoalNudgeJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed StaleGoalNudgeJob: {Count} nudge(s) across {TenantCount} tenant(s)",
            total, tenantIds.Count);
    }
}
