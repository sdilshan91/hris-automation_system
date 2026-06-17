using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default generic <see cref="IEmailSender"/> (US-NTF-002 FR-8). Logs the email it WOULD send and treats that as a
/// successful send — mirroring the platform's other log-only seams (<see cref="LogOnlyPayslipEmailSender"/>,
/// <see cref="LogOnlyTenantWelcomeEmailService"/>). There is no real transactional-email infra yet; wiring a real
/// SMTP/transactional sender is a one-class swap. This sender NEVER throws and is NEVER a hard startup/test
/// dependency, so the app starts and tests run with no SMTP configured.
/// </summary>
public sealed class LogOnlyEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LogOnlyEmailSender> _logger;

    public LogOnlyEmailSender(IConfiguration configuration, ILogger<LogOnlyEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var smtpHost = _configuration["Smtp:Host"];

        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            _logger.LogInformation(
                "[EMAIL-STUB] Would send email to {Recipient} (tenant {TenantId}). Subject='{Subject}'. " +
                "Configure Smtp:Host to enable real delivery.",
                message.RecipientEmail, message.TenantId, message.Subject);
            return Task.CompletedTask;
        }

        // TODO(US-NTF): dispatch a real SMTP/transactional email when SMTP is configured. The From address is
        // message.FromAddress (tenant sender domain, BR-4) or the system default.
        _logger.LogInformation(
            "Sending email to {Recipient} (tenant {TenantId}). Subject='{Subject}'.",
            message.RecipientEmail, message.TenantId, message.Subject);

        return Task.CompletedTask;
    }
}
