namespace HRM.Domain.Enums;

/// <summary>
/// The delivery channel a <see cref="Entities.NotificationDelivery"/> row records (US-NTF-006). Domain-level
/// mirror of the Application <c>NotificationChannel</c> seam (Domain has no dependency on Application). Stored as
/// a string column (the codebase's varchar-enum pattern) for readability/forward-compat.
/// </summary>
public enum NotificationDeliveryChannel
{
    /// <summary>Real-time in-app notification (persisted row + SignalR push).</summary>
    InApp = 0,

    /// <summary>Transactional email (rendered template, sent via the Hangfire SendEmailJob).</summary>
    Email = 1,
}
