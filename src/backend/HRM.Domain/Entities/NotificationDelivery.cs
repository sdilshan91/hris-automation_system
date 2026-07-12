using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Delivery-tracking row for a single notification send (US-NTF-006 delivery infrastructure). One row per
/// (recipient, channel) attempt group: the <see cref="RealNotificationDispatcher"/> writes it and the Hangfire
/// <c>SendEmailJob</c> drives it to a terminal state. Mirrors the <see cref="OnboardingNotificationOutbox"/>
/// shape (status / attempts / last-error / sent-at) so operators can see what was delivered, suppressed by
/// preferences, deferred by quiet-hours, or failed after retries.
///
/// <para>Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the global query filter. The dispatcher may run
/// outside a resolved tenant context (from the notification outbox worker), so <see cref="BaseEntity.TenantId"/>
/// is set EXPLICITLY on write (mirroring <c>SignalRNotificationService</c>). Maps to "notification_delivery".</para>
/// </summary>
public sealed class NotificationDelivery : BaseEntity, IAuditExempt
{
    /// <summary>The channel this row tracks (Email / InApp).</summary>
    public NotificationDeliveryChannel Channel { get; set; }

    /// <summary>Delivery lifecycle state (Queued/Deferred → Sent | Failed | Suppressed).</summary>
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Queued;

    /// <summary>Number of send attempts (incremented by the send job on each try; drives terminal Failed).</summary>
    public int Attempts { get; set; }

    /// <summary>Last send error message, if any (truncated).</summary>
    public string? LastError { get; set; }

    /// <summary>When the notification was successfully sent (null until then).</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>The raw notification type the caller dispatched, e.g. "onboarding.checklist.assigned".</summary>
    public string NotificationType { get; set; } = string.Empty;

    /// <summary>The resolved email-template catalog event key used to render the message (e.g. "onboarding_welcome").</summary>
    public string EventKey { get; set; } = string.Empty;

    /// <summary>The recipient user (global User id — the same id carried on the in-app notification row).</summary>
    public Guid RecipientUserId { get; set; }

    /// <summary>The resolved recipient email address (null for in-app rows or when it could not be resolved).</summary>
    public string? RecipientEmail { get; set; }

    /// <summary>The rendered subject line (email rows), for operator visibility.</summary>
    public string? Subject { get; set; }
}
