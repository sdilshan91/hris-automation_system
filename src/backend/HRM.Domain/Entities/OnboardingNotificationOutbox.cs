using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Notification-intent outbox row for onboarding (US-ONB-002 NFR-3, AC-2/AC-5, FR-4). One row per
/// recipient is written in the SAME transaction as the checklist assignment; a Hangfire worker later
/// reads <see cref="OnboardingNotificationStatus.Pending"/> rows and dispatches them (in-app + email)
/// via <c>INotificationDispatcher</c>. This decouples assignment durability from notification delivery
/// while the Notifications module (US-NTF-001/002) does not yet exist. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the global query filter. Maps to "onboarding_notification_outbox".
/// </summary>
public sealed class OnboardingNotificationOutbox : BaseEntity, IAuditExempt
{
    /// <summary>The checklist instance this notification relates to.</summary>
    public Guid ChecklistInstanceId { get; set; }

    /// <summary>The recipient user (resolved responsible party / the new hire / the manager).</summary>
    public Guid RecipientUserId { get; set; }

    /// <summary>The recipient's role on the checklist (Manager / IT / Employee / HR), for templating.</summary>
    public OnboardingResponsibleRole RecipientRole { get; set; }

    /// <summary>Notification kind, e.g. "onboarding.checklist.assigned" — the dispatcher branches on this.</summary>
    public string NotificationType { get; set; } = string.Empty;

    /// <summary>Serialized JSON payload (employee, template, task count, due dates) for templating.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Dispatch state (NFR-3). Written <see cref="OnboardingNotificationStatus.Pending"/>.</summary>
    public OnboardingNotificationStatus Status { get; set; } = OnboardingNotificationStatus.Pending;

    /// <summary>When the worker successfully dispatched the notification (null until then).</summary>
    public DateTime? DispatchedAt { get; set; }

    /// <summary>Number of dispatch attempts (for retry/backoff visibility).</summary>
    public int AttemptCount { get; set; }

    /// <summary>Last dispatch error message, if any.</summary>
    public string? LastError { get; set; }
}
