using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ATT-007 FR-7: Hangfire background job for large monthly-summary exports (&gt; 1,000 employees).
/// Enqueued by <c>AttendanceSummaryService.ExportAsync</c>. Restores the tenant context into its own
/// scope (so the EF global query filter applies), regenerates the summary under the same month/filter,
/// renders the file via the SAME renderer used by the synchronous path (byte-identical), stores it
/// via the <see cref="IReportExportStorage"/> seam, and notifies the requester (in-app + email) that the
/// export is ready to download (US-NTF-006, via <see cref="INotificationDispatcher"/>) — completing FR-7.
/// </summary>
public sealed class AttendanceSummaryExportJob : IAttendanceSummaryExportJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AttendanceSummaryExportJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync(
        Guid tenantId,
        Guid reportId,
        Guid requestedByUserId,
        int year,
        int month,
        string format,
        MonthlySummaryFilter filter,
        CancellationToken cancellationToken)
    {
        Log.Information(
            "AttendanceSummaryExportJob starting: report {ReportId} ({Year}-{Month}/{Format}) for tenant {TenantId}",
            reportId, year, month, format, tenantId);

        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var service = scope.ServiceProvider.GetRequiredService<IAttendanceSummaryService>();
        var storage = scope.ServiceProvider.GetRequiredService<IReportExportStorage>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // RLS increment 2c: run the export via the shared runner so it sets the tenant context (and, gated on
        // Rls:Enabled, the app.current_tenant GUC) — this export-by-id job stays inside the RLS backstop.
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async ct =>
        {
            var summary = await service.GetMonthlyAsync(year, month, filter, ct);
            if (summary.IsFailure)
            {
                Log.Error("AttendanceSummaryExportJob {ReportId} failed to generate: {Error}",
                    reportId, summary.Error);
                return;
            }

            var (content, fileName, contentType) = service.RenderExport(year, month, format, summary.Value!);
            var location = await storage.SaveAsync(tenantId, reportId, fileName, contentType, content, ct);

            // US-NTF-006 (FR-7): notify the requester their export is ready (in-app + email) with the download
            // locator. Guarded so a delivery failure never fails the export job.
            await DispatchReportReadyAsync(dispatcher, tenantId, requestedByUserId, year, month, location);

            Log.Information(
                "AttendanceSummaryExportJob {ReportId} complete: {Rows} rows, {Bytes} bytes stored at {Location}. " +
                "Requester {UserId} notified.",
                reportId, summary.Value!.Rows.Count, content.Length, location, requestedByUserId);
        }, cancellationToken);
    }

    /// <summary>
    /// Sends an <c>attendance_summary_export_ready</c> notification (in-app + email) to the requester carrying the
    /// report period (YYYY-MM) + download locator (US-NTF-006 / FR-7). Guarded so a delivery failure never aborts
    /// the export. Skipped when no requester id is available (defensive — an enqueue always threads the caller's id).
    /// </summary>
    // internal (not private) so AttendanceSummaryExportJobNotificationTests can exercise the dispatch leg directly,
    // mirroring LeaveReportExportJobNotificationTests (US-NTF-006 export-ready test-symmetry).
    internal static async Task DispatchReportReadyAsync(
        INotificationDispatcher dispatcher, Guid tenantId, Guid requestedByUserId,
        int year, int month, string downloadUrl)
    {
        if (requestedByUserId == Guid.Empty)
            return;

        var period = $"{year:0000}-{month:00}";
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "Your attendance summary export is ready",
            ["message"] =
                $"Your {period} attendance summary has been generated and is ready to download.",
            ["report"] = new Dictionary<string, object?>
            {
                ["type"] = period,
                ["downloadUrl"] = downloadUrl,
            },
        });

        try
        {
            var request = new NotificationRequest(
                TenantId: tenantId, EventKey: "attendance_summary_export_ready", PayloadJson: payloadJson,
                RecipientUserId: requestedByUserId, NotificationType: "attendance_summary_export_ready");
            await dispatcher.SendInAppAsync(request, CancellationToken.None);
            await dispatcher.SendEmailAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "AttendanceSummaryExportJob: failed to dispatch attendance_summary_export_ready to user {UserId} (tenant {TenantId}).",
                requestedByUserId, tenantId);
        }
    }
}
