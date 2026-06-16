using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that reminds 360-degree reviewers who have NOT yet submitted their feedback while
/// the cycle's feedback window is open (US-PRF-005 AC-5/FR-8). For each active/trial tenant it sets the
/// tenant context (so the EF global query filters apply) and delegates to
/// <see cref="IFeedback360ReminderService.SendDueRemindersAsync"/>, which dispatches via the existing
/// performance notification seam (log-only impl).
///
/// Mirrors the tenant-iteration structure of <see cref="SelfAssessmentReminderJob"/>: idempotent per run and
/// tenant-safe. Real in-app/email delivery is deferred to US-NTF (the notification seam is log-only).
/// </summary>
public sealed class Feedback360ReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public Feedback360ReminderJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting Feedback360ReminderJob");

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

                var reminders = scope.ServiceProvider.GetRequiredService<IFeedback360ReminderService>();
                var sent = await reminders.SendDueRemindersAsync(now);
                total += sent;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Feedback360ReminderJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed Feedback360ReminderJob: {Count} reminder(s) dispatched across {TenantCount} tenant(s)",
            total, tenantIds.Count);
    }
}
