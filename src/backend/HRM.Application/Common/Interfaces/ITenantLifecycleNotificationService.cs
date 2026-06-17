namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-004 (AC-1/AC-3/AC-5/AC-6, FR-1/FR-2/FR-5): the dispatch seam for tenant lifecycle notifications
/// (suspension / termination-initiated / reactivation / restoration) sent to the tenant's billing email and
/// Tenant Admin users. Mirrors the platform's other log-only notification seams
/// (<see cref="IImpersonationNotificationService"/>, <see cref="ITenantWelcomeEmailService"/>): there is no
/// real email/in-app channel yet (TODO US-NTF), so the default implementation logs the message it WOULD send
/// and treats that as a successful dispatch. Never throws, so a transition is never blocked by a stubbed
/// notification failure. Wiring real delivery is a one-class swap.
/// </summary>
public interface ITenantLifecycleNotificationService
{
    Task NotifyLifecycleChangeAsync(
        TenantLifecycleNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// The lifecycle-notification payload (US-ADM-004). <see cref="EventType"/> is the lowercase lifecycle event
/// ("suspended" / "termination_initiated" / "reactivated" / "restored"). Recipients are the billing email and
/// the Tenant Admin user emails.
/// </summary>
public sealed record TenantLifecycleNotification(
    Guid TenantId,
    string TenantName,
    string TenantSubdomain,
    string EventType,
    string? Reason,
    DateTime? TerminationScheduledAt,
    string? BillingEmail,
    IReadOnlyList<string> AdminEmails);
