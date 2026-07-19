namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Seam for sending account lockout notification emails (US-AUTH-010 FR-8).
/// Implementations may send via SMTP, or log a stub when SMTP is not configured.
/// </summary>
public interface ILockoutNotificationService
{
    /// <summary>
    /// Sends a lockout notification email to the user.
    /// </summary>
    Task SendLockoutNotificationAsync(
        string userEmail,
        string? displayName,
        DateTime lockedUntilUtc,
        int lockoutDurationMinutes,
        // ISSUE-063: the login-time resolved tenant name, threaded through for email branding/sign-off. Null when
        // the tenant could not be resolved (content degrades gracefully). Trailing data param, BEFORE the token.
        string? tenantName,
        // DF-40: the login-time resolved tenant id, needed to stamp the outgoing EmailMessage (branding/logging;
        // works from the Hangfire job path with no resolved ITenantContext). Null/Guid.Empty when unresolved.
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}
