using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Daily Hangfire recurring job that auto-closes performance reviews the employee never signed within the
/// cycle's configurable window (US-PRF-006 BR-3, default 7 days). For each active/trial tenant it sets the
/// tenant context (so the EF global query filters apply) and delegates to
/// <see cref="IReviewSignoffAutoCloseService.AutoCloseOverdueAsync"/>, which flips overdue reviews to
/// NoResponse, appends an immutable AutoClosedNoResponse sign-off and notifies HR via the performance
/// notification seam (real delivery, US-NTF-006 Phase 5b).
///
/// Mirrors the tenant-iteration structure of <see cref="SelfAssessmentReminderJob"/>: idempotent per day and
/// tenant-safe.
/// <para><b>Corrected 2026-08-18:</b> this header described the performance notification seam as "log-only" / "deferred to US-NTF". That is stale — <c>DependencyInjection.cs:597</c> registers <c>RealPerformanceNotificationService</c> (US-NTF-006 Phase 5b), so this job's notifications are really delivered. <c>LogOnly</c> survives only as a sibling that integration tests register. Verified before editing, not assumed.</para>
/// </summary>
public sealed class ReviewSignoffAutoCloseJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ReviewSignoffAutoCloseJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ReviewSignoffAutoCloseJob");

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
                var autoClose = scope.ServiceProvider.GetRequiredService<IReviewSignoffAutoCloseService>();

                // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant context
                // (and, gated on Rls:Enabled, the app.current_tenant GUC) — keeping it inside the RLS backstop.
                await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
                {
                    total += await autoClose.AutoCloseOverdueAsync(now);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ReviewSignoffAutoCloseJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed ReviewSignoffAutoCloseJob: {Count} review(s) auto-closed across {TenantCount} tenant(s)",
            total, tenantIds.Count);
    }
}
