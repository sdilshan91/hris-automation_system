// ============================================================================
// US-NTF-006 Phase 4 — RealPayslipEmailSender unit tests (US-PAY-011 FR-2/NFR-2/NFR-5).
//
// The Real sender maps the prebuilt PayslipEmailMessage (subject/body carry NO salary) + the payslip PDF onto the
// generic IEmailSender seam and sends it INLINE (not via the dispatcher). The load-bearing behaviour is the
// transient-fault TRANSLATION: the distribution runner's Polly retry only retries PayslipEmailTransientException,
// so this sender must translate MailKit's transient faults into that type and let permanent faults propagate
// UNCHANGED. These tests genuinely observe that exception-type translation.
//
//   #1 Attachment/mapping   — captures the EmailMessage handed to a fake IEmailSender: recipient/subject, exactly
//                              one application/pdf attachment (bytes+filename), body passed through (no salary, NFR-5).
//   #2 Transient mapping    — a 4xx SmtpCommandException / SmtpProtocolException / Socket / IO / Timeout each throw
//                              PayslipEmailTransientException; a 5xx SmtpCommandException and a generic Exception
//                              propagate as-is (SAME instance — proving they are NOT wrapped).
//   #3 LogOnly path         — with the real never-throwing LogOnlyEmailSender, SendAsync completes (no-SMTP records Sent).
//
// PROVIDER: a hand fake IEmailSender (capture + optional throw); no SMTP server. MailKit's SmtpCommandException is
// constructed via its public (SmtpErrorCode, SmtpStatusCode, string) ctor — MailboxBusy=450 (4xx transient) /
// MailboxUnavailable=550 (5xx permanent) — so BOTH the 4xx and 5xx arms are really constructed and exercised.
// ============================================================================

using System.Net.Sockets;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Payroll;
using HRM.Infrastructure.Services;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class RealPayslipEmailSenderTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private static readonly byte[] Pdf = "%PDF-1.4 fake-payslip-bytes"u8.ToArray();

    /// <summary>Captures the single EmailMessage handed to the seam; optionally throws a configured fault on send.</summary>
    private sealed class FakeEmailSender : IEmailSender
    {
        private readonly Exception? _throw;
        public EmailMessage? Captured { get; private set; }
        public int Calls { get; private set; }
        public FakeEmailSender(Exception? throwOnSend = null) => _throw = throwOnSend;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Calls++;
            Captured = message;
            if (_throw is not null) throw _throw;
            return Task.CompletedTask;
        }
    }

    // A realistic payslip message built from the real template (NFR-5: no salary in subject/body).
    private PayslipEmailMessage Message() => new(
        TenantId: _tenant,
        RecipientEmail: "jane.doe@acme.test",
        RecipientName: "Jane Doe",
        Subject: PayslipEmailTemplate.BuildSubject(5, 2026),
        Body: PayslipEmailTemplate.BuildBody("Jane Doe", 5, 2026, "Acme Corp"),
        AttachmentFileName: "Payslip-May-2026.pdf",
        AttachmentContent: Pdf,
        AttachmentContentType: "application/pdf",
        FromAddress: null);

    private static RealPayslipEmailSender Sender(IEmailSender inner) =>
        new(inner, NullLogger<RealPayslipEmailSender>.Instance);

    // ── #1: mapping — recipient/subject, exactly one PDF attachment, body passed through (no salary) ──

    [Fact]
    public async Task SendAsync_MapsMessageOntoEmailSender_WithSinglePdfAttachment_AndNoSalary()
    {
        var fake = new FakeEmailSender();
        var msg = Message();

        await Sender(fake).SendAsync(msg);

        fake.Calls.Should().Be(1);
        var sent = fake.Captured!;
        sent.RecipientEmail.Should().Be("jane.doe@acme.test");
        sent.Subject.Should().Be(msg.Subject);
        // The prebuilt plain-text body is passed through unchanged (mapping introduces no salary — NFR-5).
        sent.BodyText.Should().Be(msg.Body);

        sent.Attachments.Should().NotBeNull();
        sent.Attachments!.Should().ContainSingle();
        var att = sent.Attachments!.Single();
        att.FileName.Should().Be("Payslip-May-2026.pdf");
        att.ContentType.Should().Be("application/pdf");
        att.Content.Should().Equal(Pdf); // the PDF bytes, byte-for-byte.

        // NFR-5 guard: neither the subject nor either body alternative carries a salary figure.
        foreach (var text in new[] { sent.Subject, sent.BodyText, sent.BodyHtml })
        {
            text.Should().NotContain("60000");
            text.Should().NotContain("70,000");
            text.Should().NotContainEquivalentOf("net salary");
        }
    }

    // ── #2: transient faults → PayslipEmailTransientException (the load-bearing translation) ──

    public static IEnumerable<object[]> TransientFaults()
    {
        yield return new object[] { (Func<Exception>)(() =>
            new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.MailboxBusy, "SMTP 450 transient")) };
        yield return new object[] { (Func<Exception>)(() => new SmtpProtocolException("protocol error")) };
        yield return new object[] { (Func<Exception>)(() => new SocketException()) };
        yield return new object[] { (Func<Exception>)(() => new IOException("connection reset")) };
        yield return new object[] { (Func<Exception>)(() => new TimeoutException("send timed out")) };
    }

    [Theory]
    [MemberData(nameof(TransientFaults))]
    public async Task SendAsync_TransientFault_IsTranslatedTo_PayslipEmailTransientException(Func<Exception> faultFactory)
    {
        var original = faultFactory();
        var sender = Sender(new FakeEmailSender(throwOnSend: original));

        var act = () => sender.SendAsync(Message());

        // The type is genuinely translated, and the original fault is preserved as the inner exception.
        (await act.Should().ThrowAsync<PayslipEmailTransientException>())
            .Which.InnerException.Should().BeSameAs(original);
    }

    // ── #2 (cont.): permanent faults propagate UNCHANGED (NOT wrapped) ──

    [Fact]
    public async Task SendAsync_5xxSmtpCommand_PropagatesUnchanged_NotWrapped()
    {
        // MailboxUnavailable = 550 → a permanent SMTP reply. Must NOT become PayslipEmailTransientException.
        var permanent = new SmtpCommandException(
            SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "SMTP 550 mailbox unavailable");
        var sender = Sender(new FakeEmailSender(throwOnSend: permanent));

        var act = () => sender.SendAsync(Message());

        (await act.Should().ThrowAsync<SmtpCommandException>())
            .Which.Should().BeSameAs(permanent); // same instance ⇒ propagated, not re-thrown/wrapped.
    }

    [Fact]
    public async Task SendAsync_GenericFault_PropagatesUnchanged_NotWrapped()
    {
        var permanent = new InvalidOperationException("hard bounce");
        var sender = Sender(new FakeEmailSender(throwOnSend: permanent));

        var act = () => sender.SendAsync(Message());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(permanent);
    }

    // ── #3: LogOnlyEmailSender (real, never throws) → SendAsync completes (no-SMTP records Sent) ──

    [Fact]
    public async Task SendAsync_WithLogOnlyEmailSender_Completes_NoThrow()
    {
        // Empty configuration ⇒ no Smtp:Host ⇒ LogOnlyEmailSender takes the log-only branch and never throws.
        var config = new ConfigurationBuilder().Build();
        var logOnly = new LogOnlyEmailSender(config, NullLogger<LogOnlyEmailSender>.Instance);

        var act = () => Sender(logOnly).SendAsync(Message());

        await act.Should().NotThrowAsync();
    }
}
