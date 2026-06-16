using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IPayslipEmailSender"/> (US-PAY-011 FR-2). Logs the message it WOULD send and treats that
/// as a successful send (SMTP acceptance, AC-2/§10) — mirroring the platform's other log-only notification
/// seams (<see cref="LockoutNotificationService"/>, the log-only <c>IPayroll/Leave/RecruitmentNotificationService</c>).
/// There is no real transactional-email infra yet (TODO US-NTF); wiring a real SMTP sender is a one-class swap.
///
/// <para>NFR-5: the log line carries only the recipient + subject + attachment file name — never the PDF bytes
/// and never a salary amount (the figures live only in the attachment). The Polly retry (NFR-2) lives in the
/// distribution runner, which retries on <see cref="PayslipEmailTransientException"/>; this log-only sender
/// never throws, so it always "succeeds".</para>
/// </summary>
public sealed class LogOnlyPayslipEmailSender : IPayslipEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LogOnlyPayslipEmailSender> _logger;

    public LogOnlyPayslipEmailSender(IConfiguration configuration, ILogger<LogOnlyPayslipEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendAsync(PayslipEmailMessage message, CancellationToken cancellationToken = default)
    {
        var smtpHost = _configuration["Smtp:Host"];

        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            // SMTP not configured -- log the email that would be sent (no PDF bytes, no salary amount).
            _logger.LogInformation(
                "[PAYSLIP-EMAIL-STUB] Would send payslip email to {Recipient} (tenant {TenantId}). " +
                "Subject='{Subject}', Attachment='{Attachment}' ({Size} bytes). Configure Smtp:Host to enable real delivery.",
                message.RecipientEmail, message.TenantId, message.Subject, message.AttachmentFileName,
                message.AttachmentContent.Length);
            return Task.CompletedTask;
        }

        // TODO(US-NTF): dispatch a real SMTP/transactional email when SMTP is configured. The From address is
        // message.FromAddress (tenant sender domain, BR-4) or the system default.
        _logger.LogInformation(
            "Sending payslip email to {Recipient} (tenant {TenantId}). Subject='{Subject}', Attachment='{Attachment}'.",
            message.RecipientEmail, message.TenantId, message.Subject, message.AttachmentFileName);

        return Task.CompletedTask;
    }
}
