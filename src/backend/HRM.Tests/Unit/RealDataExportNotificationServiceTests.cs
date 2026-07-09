// ============================================================================
// US-NTF-006 Phase 4 — RealDataExportNotificationService unit tests (US-ADM-010 AC-2/BR-6).
//
// The Real service dispatches the "your export bundle is ready" email (with the download link) to each RAW
// recipient address (requester + tenant billing contact — may have no User row) via the dispatcher's email-only
// leg. It NEVER throws into the export-generation pipeline.
//
//   #4a  Two recipients → SendEmailAsync fires once per recipient, EventKey="data_export_ready",
//        RecipientEmail = each address, payload carries the downloadUrl. No in-app leg (raw addresses).
//   #4b  Never-throw when the dispatcher throws.
//   #4c  Empty / duplicate recipients are de-duplicated / skipped gracefully.
//
// PROVIDER: a hand RecordingDispatcher (records requests; optionally throws). No SMTP / DB.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class RealDataExportNotificationServiceTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _exportId = Guid.NewGuid();
    private const string DownloadUrl = "https://app.example.com/exports/019f2607/download?token=abc123";

    /// <summary>Records every dispatched request per leg; optionally throws to prove the never-throw contract.</summary>
    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        private readonly bool _throw;
        public List<NotificationRequest> InApp { get; } = new();
        public List<NotificationRequest> Email { get; } = new();
        public RecordingDispatcher(bool throwOnDispatch = false) => _throw = throwOnDispatch;

        public Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("in-app dispatch boom");
            InApp.Add(request);
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("email dispatch boom");
            Email.Add(request);
            return Task.CompletedTask;
        }
    }

    private static RealDataExportNotificationService Service(INotificationDispatcher dispatcher) =>
        new(dispatcher, NullLogger<RealDataExportNotificationService>.Instance);

    // ── #4a: one email per recipient, correct event key + payload, no in-app leg ──

    [Fact]
    public async Task SendExportReadyAsync_TwoRecipients_SendsOneEmailEach_WithDownloadUrl()
    {
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).SendExportReadyAsync(
            _tenant, _exportId,
            recipients: ["requester@acme.test", "billing@acme.test"],
            downloadUrl: DownloadUrl);

        dispatcher.Email.Should().HaveCount(2);
        dispatcher.InApp.Should().BeEmpty("raw addresses may have no User row → email-only leg");

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "data_export_ready");
        dispatcher.Email.Should().OnlyContain(r => r.TenantId == _tenant);
        dispatcher.Email.Select(r => r.RecipientEmail)
            .Should().BeEquivalentTo("requester@acme.test", "billing@acme.test");
        // The download link travels in the JSON payload for every recipient.
        dispatcher.Email.Should().OnlyContain(r => r.PayloadJson.Contains(DownloadUrl));
    }

    // ── #4b: never-throw when the dispatcher throws (export is already completed) ──

    [Fact]
    public async Task SendExportReadyAsync_DispatcherThrows_DoesNotThrow()
    {
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).SendExportReadyAsync(
            _tenant, _exportId, recipients: ["requester@acme.test"], downloadUrl: DownloadUrl);

        await act.Should().NotThrowAsync();
    }

    // ── #4c: empty/duplicate recipient list → nothing sent / de-duplicated, still never throws ──

    [Fact]
    public async Task SendExportReadyAsync_EmptyRecipients_SendsNothing_NoThrow()
    {
        var dispatcher = new RecordingDispatcher();

        var act = () => Service(dispatcher).SendExportReadyAsync(
            _tenant, _exportId, recipients: ["", "   "], downloadUrl: DownloadUrl);

        await act.Should().NotThrowAsync();
        dispatcher.Email.Should().BeEmpty();
    }

    [Fact]
    public async Task SendExportReadyAsync_DuplicateRecipients_AreDeduplicated_CaseInsensitively()
    {
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).SendExportReadyAsync(
            _tenant, _exportId,
            recipients: ["User@Acme.Test", "user@acme.test", "  user@acme.test  "],
            downloadUrl: DownloadUrl);

        dispatcher.Email.Should().ContainSingle();
    }
}
