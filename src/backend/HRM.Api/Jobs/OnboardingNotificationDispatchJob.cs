using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire worker that drains the onboarding notification outbox (US-ONB-002 NFR-3, AC-2/AC-5, FR-4).
/// Enqueued by <c>OnboardingChecklistService.AssignAsync</c> after the assignment transaction commits.
/// Reads <see cref="OnboardingNotificationStatus.Pending"/> rows for the tenant and dispatches each via
/// <see cref="INotificationDispatcher"/> (in-app + email), marking the row dispatched/failed.
///
/// Runs at system level (outside a request scope), so it bypasses the tenant query filter via
/// IgnoreQueryFilters() and scopes by the passed tenant id explicitly.
/// TODO(US-NTF-001/002): once the Notifications module lands, the injected dispatcher delivers for real.
/// </summary>
public sealed class OnboardingNotificationDispatchJob : IOnboardingNotificationDispatchJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OnboardingNotificationDispatchJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // RLS increment 2c: run the drain via the shared runner so it sets the tenant context (and, gated on
        // Rls:Enabled, the app.current_tenant GUC) — this outbox-by-tenant job stays inside the RLS backstop.
        // (Enqueued standalone by OnboardingChecklistService AND called from OnboardingOverdueSweepJob; each call
        // creates its own scope + runner, so there is never a nested transaction.)
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async ct =>
        {
        var pending = await dbContext.OnboardingNotificationOutbox
            .IgnoreQueryFilters()
            .Where(o => !o.IsDeleted
                && o.TenantId == tenantId
                && o.Status == OnboardingNotificationStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        Log.Information(
            "OnboardingNotificationDispatchJob: dispatching {Count} pending notification(s) for tenant {TenantId}.",
            pending.Count, tenantId);

        foreach (var row in pending)
        {
            row.AttemptCount++;
            try
            {
                var request = new NotificationRequest(
                    TenantId: row.TenantId,
                    EventKey: MapOutboxTypeToEventKey(row.NotificationType),
                    PayloadJson: row.Payload,
                    RecipientUserId: row.RecipientUserId,
                    NotificationType: row.NotificationType);

                await dispatcher.SendInAppAsync(request, ct);
                await dispatcher.SendEmailAsync(request, ct);

                row.Status = OnboardingNotificationStatus.Dispatched;
                row.DispatchedAt = DateTime.UtcNow;
                row.LastError = null;
            }
            catch (Exception ex)
            {
                row.Status = OnboardingNotificationStatus.Failed;
                row.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                Log.Error(ex,
                    "OnboardingNotificationDispatchJob: failed to dispatch outbox row {RowId} for tenant {TenantId}.",
                    row.Id, tenantId);
            }
        }

        await dbContext.SaveChangesAsync(ct);
        Log.Information("OnboardingNotificationDispatchJob: completed for tenant {TenantId}.", tenantId);
        }, cancellationToken);
    }

    /// <summary>
    /// Maps this outbox's free-form <c>NotificationType</c> (e.g. "onboarding.checklist.assigned") to a
    /// <c>NotificationEventCatalog</c> event key. Kept as a tiny local map so the outbox schema stays as-is
    /// (US-NTF-006). Every onboarding intent renders from a dedicated template whose placeholders match its
    /// outbox payload (US-NTF-006 Phase 3 — no blank {{placeholders}}); the "onboarding_welcome" default is a
    /// last-resort fallback for any future/unmapped intent.
    /// </summary>
    private static string MapOutboxTypeToEventKey(string notificationType) => notificationType switch
    {
        "onboarding.checklist.assigned" => "onboarding_checklist_assigned",
        "onboarding.task.completed" => "onboarding_task_completed",
        "onboarding.task.overdue" => "onboarding_task_overdue",
        _ => "onboarding_welcome",
    };
}
