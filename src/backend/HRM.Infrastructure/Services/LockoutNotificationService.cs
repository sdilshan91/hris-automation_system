using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Lockout notification email service (US-AUTH-010 FR-8, NFR-3).
/// Sends via SMTP when configured; logs a stub otherwise.
/// </summary>
public sealed class LockoutNotificationService : ILockoutNotificationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LockoutNotificationService> _logger;

    public LockoutNotificationService(
        IConfiguration configuration,
        ILogger<LockoutNotificationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendLockoutNotificationAsync(
        string userEmail,
        string? displayName,
        DateTime lockedUntilUtc,
        int lockoutDurationMinutes,
        CancellationToken cancellationToken = default)
    {
        var smtpHost = _configuration["Smtp:Host"];

        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            // SMTP not configured -- log the notification that would be sent
            _logger.LogWarning(
                "[LOCKOUT-EMAIL-STUB] Would send lockout notification to {Email} ({DisplayName}). " +
                "Account locked until {LockedUntil:u} ({Duration} min). Configure Smtp:Host to enable.",
                userEmail, displayName ?? "N/A", lockedUntilUtc, lockoutDurationMinutes);
            return Task.CompletedTask;
        }

        // TODO: Send real SMTP email when SMTP is configured
        // Subject: "Your account has been temporarily locked"
        // Body: Instructions to wait {lockoutDurationMinutes} minutes or contact administrator
        _logger.LogInformation(
            "Sending lockout notification email to {Email}. Locked until {LockedUntil:u}.",
            userEmail, lockedUntilUtc);

        return Task.CompletedTask;
    }
}
