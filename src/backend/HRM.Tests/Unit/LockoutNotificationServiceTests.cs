// ============================================================================
// DF-40 — LockoutNotificationService now WIRED onto the generic IEmailSender seam (US-AUTH-010 FR-8).
//
// The service used to be a log-only stub (it built the content but never sent it). It now hands the fully-rendered
// content to IEmailSender.SendAsync. These tests observe that real wiring against a recording/throwing fake sender:
//
//   #1 Dispatch/mapping — exactly one SendAsync call; recipient == userEmail; non-empty subject; the body carries the
//                          lockout duration / unlock time; the EmailMessage.TenantId == the passed tenant id.
//   #2 Null tenant id   — a null tenantId is stamped as Guid.Empty on the outgoing message.
//   #3 Propagation      — a throwing IEmailSender propagates (NOT swallowed): this method runs as a Hangfire job, so a
//                          propagated transient fault becomes a Hangfire retry (delivery durability).
//
// PROVIDER: a hand RecordingEmailSender (capture + optional throw); no SMTP, no DB — a pure unit test.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class LockoutNotificationServiceTests
{
    /// <summary>Captures the single EmailMessage handed to the seam; optionally throws a configured fault on send.</summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly Exception? _throw;
        public EmailMessage? Captured { get; private set; }
        public int Calls { get; private set; }
        public RecordingEmailSender(Exception? throwOnSend = null) => _throw = throwOnSend;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Calls++;
            Captured = message;
            if (_throw is not null) throw _throw;
            return Task.CompletedTask;
        }
    }

    private static LockoutNotificationService Service(IEmailSender sender) =>
        new(new ConfigurationBuilder().Build(), sender, NullLogger<LockoutNotificationService>.Instance);

    private const string UserEmail = "locked.user@acme.test";

    // ── #1: the assembled content is dispatched onto IEmailSender with the right recipient/subject/body/tenant ──

    [Fact]
    public async Task SendLockoutNotificationAsync_DispatchesOnce_WithMappedRecipientSubjectBodyAndTenant()
    {
        var recorder = new RecordingEmailSender();
        var tenantId = Guid.NewGuid();
        var lockedUntil = new DateTime(2026, 07, 19, 10, 30, 00, DateTimeKind.Utc);

        await Service(recorder).SendLockoutNotificationAsync(
            userEmail: UserEmail,
            displayName: "Locked User",
            lockedUntilUtc: lockedUntil,
            lockoutDurationMinutes: 15,
            tenantName: "Acme Corp",
            tenantId: tenantId);

        recorder.Calls.Should().Be(1);
        var sent = recorder.Captured!;
        sent.RecipientEmail.Should().Be(UserEmail);
        sent.Subject.Should().NotBeNullOrWhiteSpace();
        sent.TenantId.Should().Be(tenantId);

        // The plain-text body must carry the lockout duration AND the unlock time (the FR-8 data fields).
        sent.BodyText.Should().Contain("15 minute");
        sent.BodyText.Should().Contain("2026"); // the restore-time stamp (formatted UTC) lands in the body.

        // A minimal HTML alternative is also present so HTML-only clients render the same content.
        sent.BodyHtml.Should().NotBeNullOrWhiteSpace();
    }

    // ── #2: a null tenant id is stamped as Guid.Empty on the outgoing message ──

    [Fact]
    public async Task SendLockoutNotificationAsync_NullTenantId_IsStampedAsGuidEmpty()
    {
        var recorder = new RecordingEmailSender();

        await Service(recorder).SendLockoutNotificationAsync(
            userEmail: UserEmail,
            displayName: null,
            lockedUntilUtc: DateTime.UtcNow.AddMinutes(30),
            lockoutDurationMinutes: 30,
            tenantName: null,
            tenantId: null);

        recorder.Calls.Should().Be(1);
        recorder.Captured!.TenantId.Should().Be(Guid.Empty);
    }

    // ── #3: a throwing sender PROPAGATES (job-retry semantics) — the send failure is NOT swallowed ──

    [Fact]
    public async Task SendLockoutNotificationAsync_WhenSenderThrows_Propagates_NotSwallowed()
    {
        var boom = new InvalidOperationException("SMTP unavailable");
        var recorder = new RecordingEmailSender(throwOnSend: boom);

        var act = () => Service(recorder).SendLockoutNotificationAsync(
            userEmail: UserEmail,
            displayName: "Locked User",
            lockedUntilUtc: DateTime.UtcNow.AddMinutes(15),
            lockoutDurationMinutes: 15,
            tenantName: "Acme Corp",
            tenantId: Guid.NewGuid());

        // Same instance ⇒ propagated, not caught/rethrown — a Hangfire retry is the intended durability path.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(boom);
        recorder.Calls.Should().Be(1);
    }
}
