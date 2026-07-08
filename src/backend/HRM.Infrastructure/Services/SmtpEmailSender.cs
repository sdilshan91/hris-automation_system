using HRM.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Real SMTP implementation of the generic <see cref="IEmailSender"/> seam (US-NTF-002 FR-8), using MailKit
/// (the recommended modern .NET SMTP client). Registered in place of <see cref="LogOnlyEmailSender"/> only when
/// <c>Smtp:Host</c> is configured (see <c>DependencyInjection</c>); with no host the app falls back to the
/// log-only seam, so it still starts and tests run with no SMTP server.
///
/// <para>Config: <c>Smtp:Host</c>, <c>Smtp:Port</c> (default 587), <c>Smtp:Username</c>/<c>Smtp:Password</c>
/// (secrets — auth skipped when blank, e.g. smtp4dev), <c>Smtp:FromAddress</c> (default = Username),
/// <c>Smtp:FromName</c>, <c>Smtp:UseStartTls</c> (default true; 587 STARTTLS for Gmail). The per-message
/// <see cref="EmailMessage.FromAddress"/> (tenant sender domain, BR-4) takes precedence over the configured
/// default when present.</para>
///
/// <para>Unlike the log-only seam this SURFACES delivery failures (throws) so the caller — typically a Hangfire
/// job — can retry / mark the send failed, per the <see cref="IEmailSender"/> contract.</para>
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var host = _configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            // Defensive: this type is only registered when Smtp:Host is set, so this should not happen.
            throw new InvalidOperationException("SmtpEmailSender requires Smtp:Host to be configured.");
        }

        var port = int.TryParse(_configuration["Smtp:Port"], out var p) ? p : 587;
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var fromAddress = message.FromAddress
            ?? _configuration["Smtp:FromAddress"]
            ?? username
            ?? throw new InvalidOperationException("No From address: set Smtp:FromAddress or Smtp:Username.");
        var fromName = _configuration["Smtp:FromName"] ?? "HRM";
        var useStartTls = !bool.TryParse(_configuration["Smtp:UseStartTls"], out var tls) || tls;

        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(fromName, fromAddress));
        email.To.Add(MailboxAddress.Parse(message.RecipientEmail));
        email.Subject = message.Subject;
        email.Body = new BodyBuilder
        {
            HtmlBody = message.BodyHtml,
            TextBody = message.BodyText,
        }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var socketOptions = useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(host, port, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(username))
            {
                await client.AuthenticateAsync(username, password, cancellationToken);
            }

            await client.SendAsync(email, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            _logger.LogInformation(
                "Sent email to {Recipient} (tenant {TenantId}) via SMTP {Host}:{Port}. Subject='{Subject}'.",
                message.RecipientEmail, message.TenantId, host, port, message.Subject);
        }
        catch (Exception ex)
        {
            // Surface to the caller (Hangfire job) for retry; never log the password.
            _logger.LogError(ex,
                "SMTP send FAILED to {Recipient} (tenant {TenantId}) via {Host}:{Port}. Subject='{Subject}'.",
                message.RecipientEmail, message.TenantId, host, port, message.Subject);
            throw;
        }
    }
}
