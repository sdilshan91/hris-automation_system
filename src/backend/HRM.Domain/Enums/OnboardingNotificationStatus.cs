namespace HRM.Domain.Enums;

/// <summary>
/// Dispatch state of an onboarding notification-outbox row (US-ONB-002 NFR-3). Intent rows are written in
/// the SAME transaction as the assignment; a Hangfire worker later picks up <see cref="Pending"/> rows and
/// dispatches them via <c>INotificationDispatcher</c>. Stored as a string column.
/// </summary>
public enum OnboardingNotificationStatus
{
    /// <summary>Written in the assignment transaction, awaiting the Hangfire dispatch worker.</summary>
    Pending = 0,

    /// <summary>Successfully dispatched (in-app + email) by the worker.</summary>
    Dispatched = 1,

    /// <summary>Dispatch failed after retries — left for inspection.</summary>
    Failed = 2,
}
