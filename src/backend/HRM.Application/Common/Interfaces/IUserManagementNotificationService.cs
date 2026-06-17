namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The send seam for tenant user-management emails (US-ADM-005): invitation emails (AC-2/FR-2/NFR-3) and
/// forced-password-reset emails (AC-5). Mirrors the platform's other log-only notification seams
/// (<see cref="ITenantWelcomeEmailService"/>, the log-only Payroll/Leave/Recruitment notification services):
/// the default implementation logs the email it WOULD send and treats "logged" as a successful send. The raw
/// one-time invitation token is passed here so the real email can embed the set-up link, but it is NEVER
/// logged. Wiring a real SMTP/transactional sender is a one-class swap (TODO US-NTF).
/// </summary>
public interface IUserManagementNotificationService
{
    /// <summary>Dispatches the invitation email for a single invitee (AC-2). The raw token is never logged.</summary>
    Task SendInvitationAsync(UserInvitationEmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Dispatches the forced-password-reset email for a user (AC-5).</summary>
    Task SendPasswordResetAsync(PasswordResetEmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>An invitation email for one invitee (US-ADM-005 AC-2/FR-2).</summary>
public sealed record UserInvitationEmailMessage(
    Guid TenantId,
    string TenantName,
    string Subdomain,
    string Email,
    string RawToken,
    DateTime ExpiresAt);

/// <summary>A forced-password-reset email for one user (US-ADM-005 AC-5).</summary>
public sealed record PasswordResetEmailMessage(
    Guid TenantId,
    string Subdomain,
    string Email);
