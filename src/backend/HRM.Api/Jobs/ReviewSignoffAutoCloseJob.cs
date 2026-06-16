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
/// notification seam (log-only until US-NTF).
///
/// Mirrors the tenant-iteration structure of <see cref="SelfAssessmentReminderJob"/>: idempotent per day and
/// tenant-safe.
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

                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
                {
                    mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
                }

                var autoClose = scope.ServiceProvider.GetRequiredService<IReviewSignoffAutoCloseService>();
                var closed = await autoClose.AutoCloseOverdueAsync(now);
                total += closed;
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
