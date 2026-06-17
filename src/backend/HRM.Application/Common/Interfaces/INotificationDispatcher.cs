namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Seam for dispatching notifications (US-ONB-002 NFR-3, FR-4, AC-2/AC-5). The onboarding outbox worker
/// reads pending intent rows and calls this to deliver each one via in-app push and email.
///
/// TODO(US-NTF-001/002): the Notifications module is not built yet. The current implementation
/// (<c>LoggingNotificationDispatcher</c>) only logs the intent. When US-NTF-001 (in-app / SignalR) and
/// US-NTF-002 (email templates) land, replace it with a real dispatcher that pushes via the SignalR hub
/// and renders + sends the onboarding email templates — without changing this contract or the outbox.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Deliver an in-app (real-time) notification to a user. Tenant id is passed explicitly because the
    /// worker runs outside a request scope (no resolved ITenantContext).
    /// </summary>
    Task SendInAppAsync(
        Guid tenantId,
        Guid recipientUserId,
        string notificationType,
        string payloadJson,
        CancellationToken cancellationToken = default);

    /// <summary>Deliver an email notification to a user (rendered from the payload + notification type).</summary>
    Task SendEmailAsync(
        Guid tenantId,
        Guid recipientUserId,
        string notificationType,
        string payloadJson,
        CancellationToken cancellationToken = default);
}
