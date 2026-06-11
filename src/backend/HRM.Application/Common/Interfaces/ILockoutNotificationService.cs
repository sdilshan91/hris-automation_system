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
        CancellationToken cancellationToken = default);
}
