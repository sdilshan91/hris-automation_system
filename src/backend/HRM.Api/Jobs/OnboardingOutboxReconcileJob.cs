using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// DF-58: durability backstop for the best-effort post-commit dispatch enqueue in
/// <c>OnboardingChecklistService.AssignAsync</c> / <c>CompleteTaskAsync</c>. Those methods write
/// <c>Pending</c> notification-outbox rows inside the assignment transaction, then fire-and-forget
/// <c>IBackgroundJobClient.Enqueue&lt;IOnboardingNotificationDispatchJob&gt;</c>. If Hangfire storage is
/// unavailable in that commit→enqueue window the enqueue is lost and those Pending rows are delivered by
/// nothing (<c>OnboardingOverdueSweepJob</c> only dispatches when it writes NEW overdue rows). This recurring
/// sweep drains any orphaned Pending outbox rows across every active tenant so a dropped enqueue self-heals
/// within one cadence.
///
/// <para><b>Why it is safe.</b> The dispatch job is idempotent on the persisted outbox watermark: it only
/// touches rows with <c>Status == Pending</c> and flips each to Dispatched/Failed before commit, so a re-run
/// never re-sends an already-delivered row. The drain is UNCONDITIONAL every run (mirrors
/// <see cref="LeaveEntitlementReconcileJob"/>); the dispatch itself no-ops when a tenant has no Pending rows.</para>
/// </summary>
public sealed class OnboardingOutboxReconcileJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OnboardingOutboxReconcileJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("Starting OnboardingOutboxReconcileJob");

        // Same source/predicate as LeaveEntitlementReconcileJob: only tenants that can have active data.
        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
        }

        Log.Information("OnboardingOutboxReconcileJob: Processing {TenantCount} tenants", tenantIds.Count);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                // Fresh scope per tenant; the dispatch job sets its own tenant context / RLS GUC internally.
                using var scope = _scopeFactory.CreateScope();
                var dispatchJob = scope.ServiceProvider.GetRequiredService<IOnboardingNotificationDispatchJob>();
                await dispatchJob.RunAsync(tenantId, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OnboardingOutboxReconcileJob: Failed to drain tenant {TenantId}", tenantId);
                // Continue with other tenants; one tenant's failure must not abort the sweep.
            }
        }

        Log.Information("Completed OnboardingOutboxReconcileJob");
    }
}
