// ============================================================================
// US-NTF-006 Phase 8 — LeaveReportExportJob.DispatchReportReadyAsync unit tests.
//
// Symmetric counterpart of BulkEmployeeImportNotificationTests for the leave-export side: the job sends a
// `leave_report_ready` notification (in-app + email) to the REQUESTER (requestedByUserId), carrying the report
// type + download locator. Guarded so a delivery failure never aborts the export; skipped when no requester id.
// Uses a hand RecordingDispatcher whose captured requests are asserted (behaviour, not call-counts).
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveReports.DTOs;

namespace HRM.Tests.Unit;

public sealed class LeaveReportExportJobNotificationTests
{
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

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();

    [Fact]
    public async Task Dispatch_SendsBothLegsToRequester_WithPopulatedPayload()
    {
        var dispatcher = new RecordingDispatcher();

        await LeaveReportExportJob.DispatchReportReadyAsync(
            dispatcher, Tenant, Requester, LeaveReportType.BalanceSummary,
            "https://app.example.com/exports/leave/abc/download?token=x");

        dispatcher.InApp.Should().ContainSingle();
        dispatcher.Email.Should().ContainSingle();
        var req = dispatcher.Email.Single();
        req.RecipientUserId.Should().Be(Requester);
        dispatcher.InApp.Single().RecipientUserId.Should().Be(Requester);
        req.TenantId.Should().Be(Tenant);
        req.EventKey.Should().Be("leave_report_ready");

        // Payload-completeness: the template renders report.type + report.downloadUrl — prove both are supplied.
        using var doc = JsonDocument.Parse(req.PayloadJson);
        var report = doc.RootElement.GetProperty("report");
        report.GetProperty("type").GetString().Should().Be("BalanceSummary");
        report.GetProperty("downloadUrl").GetString()
            .Should().Be("https://app.example.com/exports/leave/abc/download?token=x");
    }

    [Fact]
    public async Task Dispatch_Skipped_WhenNoRequesterId()
    {
        var dispatcher = new RecordingDispatcher();

        await LeaveReportExportJob.DispatchReportReadyAsync(
            dispatcher, Tenant, Guid.Empty, LeaveReportType.Utilization, "https://x/y");

        dispatcher.InApp.Should().BeEmpty();
        dispatcher.Email.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_NeverThrows_WhenDispatcherFails()
    {
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => LeaveReportExportJob.DispatchReportReadyAsync(
            dispatcher, Tenant, Requester, LeaveReportType.BalanceSummary, "https://x/y");

        await act.Should().NotThrowAsync();
    }
}
