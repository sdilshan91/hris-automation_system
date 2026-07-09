namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.NotificationDelivery"/> row (US-NTF-006). Mirrors the onboarding-outbox
/// status pattern. Stored as a string column (the codebase's varchar-enum pattern).
///
/// <para>Email leg: <see cref="Queued"/> (row written, Hangfire SendEmailJob enqueued) or <see cref="Deferred"/>
/// (quiet-hours, scheduled for later) → <see cref="Sent"/> on success, or <see cref="Failed"/> once the job's
/// retries are exhausted. <see cref="Suppressed"/> is terminal: the user disabled this channel/category and the
/// notification is not a non-suppressible security type (BR-1), so nothing is sent.</para>
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Row written and the send job enqueued; not yet sent.</summary>
    Queued = 0,

    /// <summary>Successfully handed to the transport (SMTP / log-only stub).</summary>
    Sent = 1,

    /// <summary>All send attempts exhausted without success (terminal).</summary>
    Failed = 2,

    /// <summary>Dropped by the preference gate — channel/category disabled and not mandatory (BR-1, terminal).</summary>
    Suppressed = 3,

    /// <summary>Quiet hours active: scheduled to send after the user's quiet-hours window (BR-5).</summary>
    Deferred = 4,
}
