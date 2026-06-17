using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ONB-003 FR-6/AC-5/BR-4: daily Hangfire job that detects past-due, not-completed onboarding tasks and
/// writes overdue notification-outbox rows (employee + HR + manager) per tenant. Runs at system level
/// (outside a request scope): it enumerates active tenants and calls <see cref="IOnboardingChecklistService"/>'s
/// per-tenant sweep in a fresh DI scope each, then enqueues the dispatch worker to deliver the new rows.
///
/// NOTE: tenant-timezone scheduling (BR-4 default 09:00 tenant-local) is NOT built — the job runs on a single
/// UTC cron and the sweep compares against UTC dates. Per-tenant timezone is deferred.
/// TODO(US-NTF-001/002): the outbox dispatcher only logs until the Notifications module lands.
/// </summary>
public sealed class OnboardingOverdueSweepJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OnboardingOverdueSweepJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("Starting OnboardingOverdueSweepJob");

        // Enumerate tenants that can have active onboarding (skip terminated/terminating).
        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await db.Tenants
                .IgnoreQueryFilters()
                .Where(t => !t.IsDeleted
                    && t.Status != TenantStatus.Terminated
                    && t.Status != TenantStatus.Terminating)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
        }

        var totalWritten = 0;
        foreach (var tenantId in tenantIds)
        {
            // Fresh scope per tenant → fresh scoped service + DbContext (no cross-tenant state bleed).
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IOnboardingChecklistService>();
            var dispatch = scope.ServiceProvider.GetService<IOnboardingNotificationDispatchJob>();

            var written = await service.RunOverdueSweepAsync(tenantId, cancellationToken);
            totalWritten += written;

            // Deliver the newly written overdue rows (the dispatcher drains all pending rows for the tenant).
            if (written > 0 && dispatch is not null)
                await dispatch.RunAsync(tenantId, cancellationToken);
        }

        Log.Information(
            "Completed OnboardingOverdueSweepJob — {Written} overdue notification(s) across {Tenants} tenant(s).",
            totalWritten, tenantIds.Count);
    }
}
