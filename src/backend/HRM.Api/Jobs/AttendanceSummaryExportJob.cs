using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ATT-007 FR-7: Hangfire background job for large monthly-summary exports (&gt; 1,000 employees).
/// Enqueued by <c>AttendanceSummaryService.ExportAsync</c>. Restores the tenant context into its own
/// scope (so the EF global query filter applies), regenerates the summary under the same month/filter,
/// renders the file via the SAME renderer used by the synchronous path (byte-identical), and stores it
/// via the <see cref="IReportExportStorage"/> seam.
///
/// DEFERRED: the "download link sent via notification" half of FR-7 — no notification infra exists.
/// TODO(US-NTF): dispatch a real "your export is ready" notification with a download link.
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

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);

        var service = scope.ServiceProvider.GetRequiredService<IAttendanceSummaryService>();
        var storage = scope.ServiceProvider.GetRequiredService<IReportExportStorage>();

        var summary = await service.GetMonthlyAsync(year, month, filter, cancellationToken);
        if (summary.IsFailure)
        {
            Log.Error("AttendanceSummaryExportJob {ReportId} failed to generate: {Error}",
                reportId, summary.Error);
            return;
        }

        var (content, fileName, contentType) = service.RenderExport(year, month, format, summary.Value!);
        var location = await storage.SaveAsync(tenantId, reportId, fileName, contentType, content, cancellationToken);

        // TODO(US-NTF): dispatch a real "your export is ready" notification with a download link.
        Log.Information(
            "AttendanceSummaryExportJob {ReportId} complete: {Rows} rows, {Bytes} bytes stored at {Location}. " +
            "User notification deferred (US-NTF).",
            reportId, summary.Value!.Rows.Count, content.Length, location);
    }
}
