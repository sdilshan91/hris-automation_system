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
/// <see cref="IReportExportStorage"/> seam, and logs a notification.
///
/// DEFERRED: real blob storage + real notification dispatch (see <see cref="IReportExportStorage"/>'s
/// local/log-only implementation). TODO(blob-storage) / TODO(notifications).
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

            // FR-5 / AC-5: notify-when-ready seam (log-only until a real notification dispatch exists).
            // TODO(notifications): dispatch a real "your export is ready" notification with a download link.
            Log.Information(
                "LeaveReportExportJob {ReportId} complete: {Rows} rows, {Bytes} bytes stored at {Location}. " +
                "User notification (log-only seam) emitted.",
                reportId, result.Value!.Rows.Count, content.Length, location);
        }, cancellationToken);
    }
}
