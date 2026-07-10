using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire job that fires at a pre-deletion reminder boundary (14d / 7d / 1d before deletion) for a
/// terminating tenant (US-ADM-004 AC-3/FR-2). Idempotent: it no-ops if the tenant is no longer Terminating (a
/// Restore de-queued it, or the job id wasn't cancelled in time). Delivery is the log-only lifecycle
/// notification seam (real email/in-app deferred, TODO US-NTF).
/// </summary>
public sealed class TenantTerminationReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantTerminationReminderJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <param name="tenantId">The terminating tenant.</param>
    /// <param name="daysBefore">How many days before deletion this reminder represents (14/7/1).</param>
    public async Task RunAsync(Guid tenantId, int daysBefore)
    {
        using var scope = _scopeFactory.CreateScope();

        // RLS increment 2c: a platform lifecycle job that reads a terminating tenant via IgnoreQueryFilters and
        // dispatches a reminder. System context → privileged (BYPASSRLS) routing under RLS + sys: cache prefix.
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetSystemContext();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null || tenant.Status != Domain.Entities.TenantStatus.Terminating)
        {
            Log.Information(
                "TenantTerminationReminderJob: tenant {TenantId} not Terminating — skipping {Days}d reminder.",
                tenantId, daysBefore);
            return;
        }

        var notification = scope.ServiceProvider.GetRequiredService<ITenantLifecycleNotificationService>();
        await notification.NotifyLifecycleChangeAsync(new TenantLifecycleNotification(
            tenant.Id, tenant.Name, tenant.Subdomain, $"termination_reminder_{daysBefore}d",
            tenant.SuspendedReason, tenant.TerminationScheduledAt, tenant.BillingEmail, Array.Empty<string>()));

        Log.Information(
            "TenantTerminationReminderJob: dispatched {Days}d reminder for tenant {TenantId}.", daysBefore, tenantId);
    }
}
