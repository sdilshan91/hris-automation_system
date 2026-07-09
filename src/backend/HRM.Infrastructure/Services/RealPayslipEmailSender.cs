using System.Net.Sockets;
using HRM.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 4: the real <see cref="IPayslipEmailSender"/> (US-PAY-011 FR-2/NFR-2/NFR-5). Replaces the
/// log-only stub — maps the prebuilt <see cref="PayslipEmailMessage"/> (subject/body carry NO salary, NFR-5) plus
/// the payslip PDF onto the generic <see cref="IEmailSender"/> seam and sends it INLINE (NOT through
/// <see cref="INotificationDispatcher"/>: the multi-MB PDF must not be persisted as a Hangfire argument, re-rendered
/// from a template, or preference-gated — the distribution runner already owns idempotency + logging).
///
/// <para><b>Transient-fault translation (NFR-2/AC-4):</b> the distribution runner's Polly retry only retries
/// <see cref="PayslipEmailTransientException"/>, so this sender translates MailKit's transient faults (an SMTP 4xx
/// command reply, a protocol error, a socket / I/O error, or a timeout) into that exception; any other fault
/// (e.g. an SMTP 5xx permanent reply) propagates unchanged and the runner records the send Failed.</para>
///
/// <para>With no <c>Smtp:Host</c> configured the injected <see cref="IEmailSender"/> is <c>LogOnlyEmailSender</c>,
/// which never throws — so the runner records Sent exactly as it did under the old log-only payslip stub.</para>
/// </summary>
public sealed class RealPayslipEmailSender : IPayslipEmailSender
{
    private readonly IEmailSender _emailSender;
    private readonly ILogger<RealPayslipEmailSender> _logger;

    public RealPayslipEmailSender(IEmailSender emailSender, ILogger<RealPayslipEmailSender> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task SendAsync(PayslipEmailMessage message, CancellationToken cancellationToken = default)
    {
        // The body is prebuilt plain text (PayslipEmailTemplate, NFR-5 — no salary). Carry it as the text body and
        // wrap it in minimal HTML so both alternatives are present; the PDF is the single attachment.
        var emailMessage = new EmailMessage(
            TenantId: message.TenantId,
            RecipientEmail: message.RecipientEmail,
            Subject: message.Subject,
            BodyHtml: ToHtml(message.Body),
            BodyText: message.Body,
            FromAddress: message.FromAddress,   // BR-4: tenant sender domain (currently system default / null).
            Attachments:
            [
                new EmailAttachment(message.AttachmentFileName, message.AttachmentContent, message.AttachmentContentType),
            ]);

        try
        {
            await _emailSender.SendAsync(emailMessage, cancellationToken);
        }
        // ── Transient faults → PayslipEmailTransientException so the runner's Polly policy retries (NFR-2/AC-4). ──
        catch (SmtpCommandException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            throw new PayslipEmailTransientException(
                $"Transient SMTP error ({(int)ex.StatusCode}) sending payslip email to {message.RecipientEmail}.", ex);
        }
        catch (SmtpProtocolException ex)
        {
            throw new PayslipEmailTransientException(
                $"Transient SMTP protocol error sending payslip email to {message.RecipientEmail}.", ex);
        }
        catch (SocketException ex)
        {
            throw new PayslipEmailTransientException(
                $"Transient network error sending payslip email to {message.RecipientEmail}.", ex);
        }
        catch (IOException ex)
        {
            throw new PayslipEmailTransientException(
                $"Transient I/O error sending payslip email to {message.RecipientEmail}.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new PayslipEmailTransientException(
                $"Timed out sending payslip email to {message.RecipientEmail}.", ex);
        }
        // Any other exception (e.g. an SMTP 5xx permanent reply) propagates as a PERMANENT failure (AC-4).
    }

    /// <summary>Wraps the prebuilt plain-text body in minimal HTML (escape + newline → break), so a real SMTP send
    /// has an HTML alternative. No salary is introduced — the body is already NFR-5-safe.</summary>
    private static string ToHtml(string body)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(body ?? string.Empty);
        return "<p>" + encoded.Replace("\n\n", "</p><p>").Replace("\n", "<br/>") + "</p>";
    }
}
