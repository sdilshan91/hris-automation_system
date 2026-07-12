using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveReports.DTOs;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire background job that generates a large leave-report export (US-LV-012 FR-5, AC-5).
///
/// Triggered by <c>LeaveReportService.ExportReportAsync</c> when a report exceeds 5,000 rows. It
/// restores the tenant context into its own scope (so the EF global query filter applies), regenerates
/// the report under the pre-resolved role scope (BR-2), renders the CSV/XLSX file via the SAME
/// rendering used by the synchronous path (so the two are byte-identical), stores it via the
/// <see cref="IReportExportStorage"/> seam, and notifies the requester (in-app + email) that the export
/// is ready to download (US-NTF-006 Phase 8, via <see cref="INotificationDispatcher"/>).
///
/// DEFERRED: real blob storage (see <see cref="IReportExportStorage"/>'s local implementation). TODO(blob-storage).
/// </summary>
public sealed class LeaveReportExportJob : ILeaveReportExportJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public LeaveReportExportJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync(
        Guid tenantId,
        Guid reportId,
        Guid requestedByUserId,
        string scopeKind,
        Guid? scopeEmployeeId,
        LeaveReportType reportType,
        ReportExportFormat format,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        Log.Information(
            "LeaveReportExportJob starting: report {ReportId} ({ReportType}/{Format}) for tenant {TenantId}",
            reportId, reportType, format, tenantId);

        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var reportService = scope.ServiceProvider.GetRequiredService<ILeaveReportService>();
        var storage = scope.ServiceProvider.GetRequiredService<IReportExportStorage>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // RLS increment 2c: run the export via the shared runner so it sets the tenant context (and, gated on
        // Rls:Enabled, the app.current_tenant GUC) — this export-by-id job stays inside the RLS backstop (BR-1).
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async ct =>
        {
            // Regenerate the FULL (unpaged) report under the captured role scope.
            var fullParams = queryParams with { Page = 1, PageSize = int.MaxValue };
            var result = await reportService.GenerateReportWithScopeAsync(
                reportType, scopeKind, scopeEmployeeId, fullParams, ct);

            if (result.IsFailure)
            {
                Log.Error("LeaveReportExportJob {ReportId} failed to generate report: {Error}",
                    reportId, result.Error);
                return;
            }

            var (content, fileName, contentType) =
                reportService.RenderExport(reportType, format, result.Value!);

            var location = await storage.SaveAsync(
                tenantId, reportId, fileName, contentType, content, ct);

            // US-NTF-006 Phase 8 (FR-5 / AC-5): notify the requester their export is ready (in-app + email) with the
            // download locator. Guarded so a delivery failure never fails the export job.
            await DispatchReportReadyAsync(dispatcher, tenantId, requestedByUserId, reportType, location);

            Log.Information(
                "LeaveReportExportJob {ReportId} complete: {Rows} rows, {Bytes} bytes stored at {Location}. " +
                "Requester {UserId} notified.",
                reportId, result.Value!.Rows.Count, content.Length, location, requestedByUserId);
        }, cancellationToken);
    }

    /// <summary>
    /// Sends a <c>leave_report_ready</c> notification (in-app + email) to the requester carrying the report type +
    /// download locator (US-NTF-006 Phase 8). Guarded so a delivery failure never aborts the export. Skipped when no
    /// requester id is available (defensive — an enqueue always threads the caller's user id).
    /// </summary>
    // internal (not private) so LeaveReportExportJobNotificationTests can exercise the dispatch leg directly,
    // mirroring BulkEmployeeImportNotificationTests (US-NTF-006 Phase 8 test-symmetry).
    internal static async Task DispatchReportReadyAsync(
        INotificationDispatcher dispatcher, Guid tenantId, Guid requestedByUserId,
        LeaveReportType reportType, string downloadUrl)
    {
        if (requestedByUserId == Guid.Empty)
            return;

        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "Your leave report export is ready",
            ["message"] =
                $"Your {reportType} leave report has been generated and is ready to download.",
            ["report"] = new Dictionary<string, object?>
            {
                ["type"] = reportType.ToString(),
                ["downloadUrl"] = downloadUrl,
            },
        });

        try
        {
            var request = new NotificationRequest(
                TenantId: tenantId, EventKey: "leave_report_ready", PayloadJson: payloadJson,
                RecipientUserId: requestedByUserId, NotificationType: "leave_report_ready");
            await dispatcher.SendInAppAsync(request, CancellationToken.None);
            await dispatcher.SendEmailAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "LeaveReportExportJob: failed to dispatch leave_report_ready to user {UserId} (tenant {TenantId}).",
                requestedByUserId, tenantId);
        }
    }
}
