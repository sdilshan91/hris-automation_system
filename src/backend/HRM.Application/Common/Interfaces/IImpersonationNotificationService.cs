namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-003 (AC-4/FR-5): the dispatch seam for the "an impersonation session has started" notification sent to
/// the target tenant's admins. The tenant admin cannot block or opt out of impersonation (§10), but is ALWAYS
/// notified. Mirrors the platform's other log-only notification seams (<see cref="ITenantWelcomeEmailService"/>,
/// the log-only <c>IPayroll/Leave/RecruitmentNotificationService</c>): there is no real email/in-app delivery
/// channel yet (TODO US-NTF), so the default implementation logs the message it WOULD send and treats that as a
/// successful dispatch. Wiring real email + in-app channels (FR-5 names both) is a one-class swap.
/// </summary>
public interface IImpersonationNotificationService
{
    /// <summary>
    /// Notifies the target tenant's admin(s) that an impersonation session has started. Never throws in the
    /// log-only default, so session initiation is never blocked by a (stubbed) notification failure.
    /// </summary>
    Task NotifyImpersonationStartedAsync(
        ImpersonationStartedNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// The "impersonation started" notification payload (US-ADM-003 AC-4/FR-5). Carries the actor, target, session
/// id, verbatim reason, and timestamps so the tenant-admin message can read: "Platform support user {actor} has
/// started an impersonation session as {target} at {startedAt}. Reason: {reason}. Reference: {sessionId}."
/// </summary>
public sealed record ImpersonationStartedNotification(
    Guid SessionId,
    Guid TargetTenantId,
    string TargetTenantName,
    Guid ImpersonatorUserId,
    string ImpersonatorEmail,
    Guid TargetUserId,
    string TargetUserEmail,
    string Reason,
    bool IsReadOnly,
    DateTime StartedAt,
    DateTime ExpiresAt);
