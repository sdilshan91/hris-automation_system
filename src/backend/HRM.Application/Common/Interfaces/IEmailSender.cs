namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The platform's GENERIC transactional-email send seam (US-NTF-002 FR-8). One message = one recipient + a
/// rendered subject/HTML/text body. This is the first generic email abstraction on the platform — the existing
/// module-specific seams (<see cref="IPayslipEmailSender"/>, <see cref="ITenantWelcomeEmailService"/>) predate it
/// and are intentionally left as-is (rewiring them onto this seam is follow-up work, TODO US-NTF).
///
/// <para><b>Never a hard dependency.</b> The default implementation (<c>LogOnlyEmailSender</c>) follows the same
/// log-only convention as the other seams: with no SMTP configured it logs the email it WOULD send and treats
/// that as a successful send, so the app starts and tests run with no SMTP server. Wiring a real SMTP /
/// transactional sender is a one-class swap.</para>
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends one email. The log-only default never throws (a stubbed send never blocks a caller). A real
    /// implementation should surface delivery failures to its caller.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// A fully-rendered email to send (US-NTF-002 FR-8). Subject/HTML/text are already merged with data — the sender
/// does not touch templates or placeholders. <paramref name="TenantId"/> is carried for logging/branding and
/// works from background jobs (no resolved ITenantContext required).
/// </summary>
public sealed record EmailMessage(
    Guid TenantId,
    string RecipientEmail,
    string Subject,
    string BodyHtml,
    string BodyText,
    string? FromAddress = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>
/// A single binary attachment on an <see cref="EmailMessage"/> (US-NTF-006 Phase 4). Optional and additive — the
/// generic notification/email flows never set it; only the payslip-email path (US-PAY-011) attaches a PDF.
/// </summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);
