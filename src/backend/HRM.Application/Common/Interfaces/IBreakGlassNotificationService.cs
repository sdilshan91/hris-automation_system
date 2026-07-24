namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-AUTH-016 FR-4/BR-4 (NFR-2): seam for alerting tenant administrators that a break-glass local login occurred
/// under SSO enforcement. Every break-glass login is a security-sensitive event that must be surfaced (to
/// discourage routine use). Dispatched via Hangfire so the alert is delivered within 60s of the event, decoupled
/// from the login request. Mirrors <see cref="ILockoutNotificationService"/>: the log-only email sender is used
/// when SMTP is not configured, so this never becomes a hard login dependency.
/// </summary>
public interface IBreakGlassNotificationService
{
    /// <summary>
    /// Sends the break-glass security alert. All parameters are primitives so the Hangfire job serializes cleanly
    /// (no service/closure capture). The source IP + timestamp let admins spot anomalous emergency-access use.
    /// </summary>
    Task SendBreakGlassAlertAsync(
        Guid tenantId,
        string? tenantName,
        Guid adminUserId,
        string adminEmail,
        string? adminDisplayName,
        string? sourceIp,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default);
}
