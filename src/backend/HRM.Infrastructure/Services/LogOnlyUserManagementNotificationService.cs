using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IUserManagementNotificationService"/> (US-ADM-005). Logs the invitation /
/// password-reset email it WOULD send and treats that as a successful send — mirroring the platform's other
/// log-only notification seams (<c>LogOnlyTenantWelcomeEmailService</c>, the log-only Payroll/Leave/Recruitment
/// notification services). The raw invitation token is NEVER logged (only its expiry). Wiring a real SMTP
/// sender is a one-class swap (TODO US-NTF).
/// </summary>
public sealed class LogOnlyUserManagementNotificationService : IUserManagementNotificationService
{
    private readonly ILogger<LogOnlyUserManagementNotificationService> _logger;

    public LogOnlyUserManagementNotificationService(ILogger<LogOnlyUserManagementNotificationService> logger)
        => _logger = logger;

    public Task SendInvitationAsync(UserInvitationEmailMessage message, CancellationToken cancellationToken = default)
    {
        // Raw token intentionally NOT logged — only the recipient + expiry.
        _logger.LogInformation(
            "[USER-INVITATION-EMAIL-STUB] Would send invitation to {Email} for tenant '{TenantName}' " +
            "({Subdomain}). Invitation link expires {Expiry:o}. Configure Smtp:Host to enable real delivery.",
            message.Email, message.TenantName, message.Subdomain, message.ExpiresAt);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(PasswordResetEmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[PASSWORD-RESET-EMAIL-STUB] Would send forced-password-reset email to {Email} (tenant {Subdomain}). " +
            "Configure Smtp:Host to enable real delivery.",
            message.Email, message.Subdomain);
        return Task.CompletedTask;
    }
}
