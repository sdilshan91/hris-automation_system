namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The send seam for an individual payslip email (US-PAY-011 FR-2). One message = one recipient + one PDF
/// attachment. This is intentionally a thin abstraction over "dispatch this email" so the distribution job
/// (<c>SendPayslipEmailsJob</c>) and its tests don't depend on SMTP.
///
/// <para><b>Reused infra / deferred SMTP:</b> the platform has no real transactional-email delivery yet — the
/// existing notification seams (e.g. <see cref="ILockoutNotificationService"/>, the log-only
/// <c>IPayroll/Leave/RecruitmentNotificationService</c>) all log-only. The default
/// implementation here (<c>LogOnlyPayslipEmailSender</c>) follows the SAME convention: it logs the message
/// (without the PDF bytes or any salary amount, NFR-5) and treats "logged" as a successful send (SMTP
/// acceptance, AC-2/§10). Wiring a real SMTP/transactional sender is a one-class swap (TODO US-NTF).</para>
///
/// <para>Transient failures (e.g. SMTP 4xx) SHALL throw <see cref="PayslipEmailTransientException"/> so the
/// caller's Polly retry policy (NFR-2) retries; any other exception is treated as a permanent failure
/// (AC-4).</para>
/// </summary>
public interface IPayslipEmailSender
{
    /// <summary>
    /// Sends one payslip email. Throws <see cref="PayslipEmailTransientException"/> on a transient/retryable
    /// error; any other thrown exception is a permanent failure. Returns normally on acceptance.
    /// </summary>
    Task SendAsync(PayslipEmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// An individual payslip email to send (US-PAY-011 FR-2/FR-3/NFR-5). The body carries NO salary amounts —
/// only the template variables {EmployeeName}/{PayMonth}/{PayYear}/{CompanyName}; the figures are only inside
/// the attached PDF.
/// </summary>
public sealed record PayslipEmailMessage(
    Guid TenantId,
    string RecipientEmail,
    string? RecipientName,
    string Subject,
    string Body,
    string AttachmentFileName,
    byte[] AttachmentContent,
    string AttachmentContentType,
    string? FromAddress);

/// <summary>
/// Signals a transient/retryable send failure (US-PAY-011 AC-4/NFR-2). The distribution job's Polly policy
/// retries on this; other exceptions are permanent failures.
/// </summary>
public sealed class PayslipEmailTransientException : Exception
{
    public PayslipEmailTransientException(string message) : base(message) { }
    public PayslipEmailTransientException(string message, Exception inner) : base(message, inner) { }
}
