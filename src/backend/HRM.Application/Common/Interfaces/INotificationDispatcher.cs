namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Seam for dispatching notifications (US-ONB-002 NFR-3; US-NTF-006 delivery infrastructure). Callers build a
/// <see cref="NotificationRequest"/> carrying the catalog <c>EventKey</c> explicitly — the dispatcher reads the
/// notification's category + mandatory flag from <c>NotificationEventCatalog</c> (single source of truth), so there
/// is no more string-guessing (the old <c>MapType</c> heuristic is gone).
///
/// <para>Recipient resolution is flexible: pass <see cref="NotificationRequest.RecipientUserId"/> for a provisioned
/// user (in-app + <c>Users.Email</c> lookup), and/or <see cref="NotificationRequest.RecipientEmail"/> to send email
/// to a raw address that may have no <c>User</c> row (e.g. a tenant billing email). The in-app leg requires a user id
/// (you cannot push in-app to a non-user); the email leg needs an address from either field.</para>
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Deliver an in-app (real-time) notification. Requires <see cref="NotificationRequest.RecipientUserId"/> — the
    /// in-app leg is skipped gracefully when it is null (you cannot push in-app to a non-user). Tenant id is passed
    /// explicitly because callers may run outside a request scope (no resolved <c>ITenantContext</c>).
    /// </summary>
    Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deliver an email notification (rendered from the request payload + the catalog event's template). The
    /// recipient address is <see cref="NotificationRequest.RecipientEmail"/> when set, else the resolved
    /// <c>Users.Email</c> for <see cref="NotificationRequest.RecipientUserId"/>.
    /// </summary>
    Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single notification-dispatch request (US-NTF-006). Carries the catalog <c>EventKey</c> (category + mandatory
/// flag are looked up from <c>NotificationEventCatalog</c>), the JSON payload merged into the template, and the
/// recipient — a provisioned user (in-app + email) and/or a raw email override for non-provisioned recipients.
/// </summary>
/// <param name="TenantId">The tenant the notification belongs to (explicit — callers may run outside a request scope).</param>
/// <param name="EventKey">The <c>NotificationEventCatalog</c> event key (drives category, mandatory, template).</param>
/// <param name="PayloadJson">The JSON payload whose fields fill the template placeholders / in-app title+message.</param>
/// <param name="RecipientUserId">The recipient user (in-app push + <c>Users.Email</c> lookup); null for email-only raw recipients.</param>
/// <param name="RecipientEmail">A raw email override for a recipient with no <c>User</c> row; falls back to the user's email when null.</param>
/// <param name="NotificationType">Optional verbatim type string recorded on the delivery-audit row; defaults to <paramref name="EventKey"/>.</param>
public sealed record NotificationRequest(
    Guid TenantId,
    string EventKey,
    string PayloadJson,
    Guid? RecipientUserId = null,
    string? RecipientEmail = null,
    string? NotificationType = null);
