using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 2a: the real <see cref="ITenantLifecycleNotificationService"/> (US-ADM-004). Replaces the
/// log-only stub — dispatches the lifecycle notification (suspended / termination-initiated / reactivated /
/// restored) to the tenant's billing email + Tenant Admin emails via <see cref="INotificationDispatcher"/>.
/// Treated as mandatory (SystemAnnouncements catalog events flagged mandatory): an account-status change is always
/// delivered.
///
/// <para>Recipients are RAW email addresses (billing + admin), so the email leg uses the dispatcher's
/// <see cref="NotificationRequest.RecipientEmail"/> override — no <c>User</c> row is required. Where a raw address
/// DOES map to an active user in the tenant, the in-app leg is added too (best-effort). Runs in the system/admin
/// context → IgnoreQueryFilters, explicitly scoped by tenant id. <b>Never throws</b> — a delivery failure must not
/// break the lifecycle transition (every leg is wrapped; exceptions are swallowed + logged).</para>
/// </summary>
public sealed class RealTenantLifecycleNotificationService : ITenantLifecycleNotificationService
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<RealTenantLifecycleNotificationService> _logger;

    public RealTenantLifecycleNotificationService(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        ILogger<RealTenantLifecycleNotificationService> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task NotifyLifecycleChangeAsync(
        TenantLifecycleNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            // Distinct raw recipient addresses = billing email + admin emails (case-insensitive).
            var recipients = new List<string>();
            if (!string.IsNullOrWhiteSpace(notification.BillingEmail))
                recipients.Add(notification.BillingEmail!.Trim());
            recipients.AddRange(notification.AdminEmails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()));
            recipients = recipients
                .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "RealTenantLifecycleNotificationService: no recipients for '{EventType}' on tenant {TenantId}; " +
                    "notification not delivered.",
                    notification.EventType, notification.TenantId);
                return;
            }

            var eventKey = MapEventKey(notification.EventType);
            var notificationType = $"tenant.lifecycle.{notification.EventType}";
            var payloadJson = BuildPayload(notification);

            // Best-effort email → active-user-in-tenant map so the in-app leg can reach real users too.
            var userIdByEmail = await ResolveUserIdsByEmailAsync(notification.TenantId, recipients, cancellationToken);

            foreach (var email in recipients)
            {
                userIdByEmail.TryGetValue(email, out var userId);  // Guid.Empty (default) when not a user → email-only.
                var recipientUserId = userId == Guid.Empty ? (Guid?)null : userId;

                var request = new NotificationRequest(
                    TenantId: notification.TenantId,
                    EventKey: eventKey,
                    PayloadJson: payloadJson,
                    RecipientUserId: recipientUserId,
                    RecipientEmail: email,
                    NotificationType: notificationType);

                await _dispatcher.SendEmailAsync(request, cancellationToken);
                if (recipientUserId is not null)
                    await _dispatcher.SendInAppAsync(request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Never throw — the lifecycle transition is already committed; a notification failure must not surface.
            _logger.LogError(ex,
                "RealTenantLifecycleNotificationService: failed to dispatch '{EventType}' notification for tenant {TenantId}.",
                notification.EventType, notification.TenantId);
        }
    }

    /// <summary>Maps the lowercase lifecycle event type to its catalog event key; unknown types map to null-safe default.</summary>
    private static string MapEventKey(string eventType) => eventType switch
    {
        "suspended" => "tenant_suspended",
        "termination_initiated" => "tenant_termination_initiated",
        "reactivated" => "tenant_reactivated",
        "restored" => "tenant_restored",
        _ => $"tenant_{eventType}",
    };

    /// <summary>
    /// Resolves which of the raw recipient emails correspond to an active user in the tenant (email → user id),
    /// for the best-effort in-app leg. System context → IgnoreQueryFilters, explicitly scoped by tenant id.
    /// </summary>
    private async Task<Dictionary<string, Guid>> ResolveUserIdsByEmailAsync(
        Guid tenantId, IReadOnlyList<string> emails, CancellationToken cancellationToken)
    {
        var activeUserIds = await _db.UserTenants
            .IgnoreQueryFilters()
            .Where(ut => ut.TenantId == tenantId && ut.Status == UserTenantStatus.Active)
            .Select(ut => ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (activeUserIds.Count == 0)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => activeUserIds.Contains(u.Id) && emails.Contains(u.Email))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in users)
            map[u.Email] = u.Id;
        return map;
    }

    private static string BuildPayload(TenantLifecycleNotification n)
    {
        var data = new Dictionary<string, object?>
        {
            ["title"] = $"Account status changed: {n.EventType}",
            ["message"] = $"Your {n.TenantName} account status changed to '{n.EventType}'."
                          + (string.IsNullOrWhiteSpace(n.Reason) ? string.Empty : $" Reason: {n.Reason}"),
            ["tenant"] = new Dictionary<string, object?>
            {
                ["name"] = n.TenantName,
                ["companyName"] = n.TenantName,
            },
            ["event"] = new Dictionary<string, object?> { ["type"] = n.EventType },
            ["reason"] = n.Reason ?? string.Empty,
        };
        if (n.TerminationScheduledAt.HasValue)
            data["termination"] = new Dictionary<string, object?>
            {
                ["scheduledAt"] = n.TerminationScheduledAt.Value.ToString("o"),
            };
        return JsonSerializer.Serialize(data);
    }
}
