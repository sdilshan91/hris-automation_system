using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Daily Hangfire recurring job for Performance Improvement Plans (US-PRF-008 FR-3/BR-4). For each active/trial
/// tenant it sets the tenant context (so the EF global query filters apply) and delegates to
/// <see cref="IPipReminderService.RunSweepAsync"/>, which dispatches checkpoint reminders (3 days before each
/// scheduled checkpoint), PIP end-date reminders, overdue-checkpoint alerts (FR-3), and flags PIPs the employee
/// did not acknowledge within 5 business days as Not Acknowledged (BR-4), all via the REAL performance
/// notification seam (until US-NTF). Mirrors the tenant-iteration structure of
/// <see cref="ReviewSignoffAutoCloseJob"/>: idempotent per day and tenant-safe.
/// <para><b>Corrected 2026-08-18:</b> this header described the performance notification seam as "log-only" / "deferred to US-NTF". That is stale — <c>DependencyInjection.cs:597</c> registers <c>RealPerformanceNotificationService</c> (US-NTF-006 Phase 5b), so this job's notifications are really delivered. <c>LogOnly</c> survives only as a sibling that integration tests register. Verified before editing, not assumed.</para>
/// </summary>
public sealed class PipReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PipReminderJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting PipReminderJob");

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
                var reminders = scope.ServiceProvider.GetRequiredService<IPipReminderService>();

                // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant context
                // (and, gated on Rls:Enabled, the app.current_tenant GUC) — keeping it inside the RLS backstop.
                await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
                {
                    total += await reminders.RunSweepAsync(now);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PipReminderJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed PipReminderJob: {Count} notification(s)/flag(s) across {TenantCount} tenant(s)",
            total, tenantIds.Count);
    }
}
