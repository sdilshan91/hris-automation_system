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
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var pending = await dbContext.OnboardingNotificationOutbox
            .IgnoreQueryFilters()
            .Where(o => !o.IsDeleted
                && o.TenantId == tenantId
                && o.Status == OnboardingNotificationStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

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

                await dispatcher.SendInAppAsync(request, cancellationToken);
                await dispatcher.SendEmailAsync(request, cancellationToken);

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

        await dbContext.SaveChangesAsync(cancellationToken);
        Log.Information("OnboardingNotificationDispatchJob: completed for tenant {TenantId}.", tenantId);
    }

    /// <summary>
    /// Maps this outbox's free-form <c>NotificationType</c> (e.g. "onboarding.checklist.assigned") to a
    /// <c>NotificationEventCatalog</c> event key. Kept as a tiny local map so the outbox schema stays as-is
    /// (US-NTF-006 Phase 2a); all current onboarding intents render from the "onboarding_welcome" template.
    /// </summary>
    private static string MapOutboxTypeToEventKey(string notificationType) => notificationType switch
    {
        _ => "onboarding_welcome",
    };
}
