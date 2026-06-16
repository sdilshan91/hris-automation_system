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
/// did not acknowledge within 5 business days as Not Acknowledged (BR-4), all via the log-only performance
/// notification seam (until US-NTF). Mirrors the tenant-iteration structure of
/// <see cref="ReviewSignoffAutoCloseJob"/>: idempotent per day and tenant-safe.
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

                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
                {
                    mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
                }

                var reminders = scope.ServiceProvider.GetRequiredService<IPipReminderService>();
                var dispatched = await reminders.RunSweepAsync(now);
                total += dispatched;
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
