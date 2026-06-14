using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Leave reports and analytics (US-LV-012). Pure read/aggregation over the existing leave data
/// (requests, ledger, entitlements, holidays, LOP) — no new entity or migration. All queries are
/// tenant-scoped via ITenantContext + EF global query filters (BR-1, NFR-3) and additionally
/// row-scoped by the caller's role (BR-2): HR/Leave.Reports sees all tenant data, a manager sees
/// their direct-report team, an employee sees only their own data.
///
/// The service COMPOSES the prior leave computations rather than re-deriving them:
///   - balance per employee/type via the US-LV-006 LeaveDashboardService formula (engine entitlement
///     + ledger components),
///   - effective entitlement via the US-LV-002 entitlement engine,
///   - LOP/absenteeism via the US-LV-011 LopService data,
///   - carry-forward via the US-LV-008 carry-forward outputs.
/// </summary>
public interface ILeaveReportService
{
    /// <summary>
    /// Generates a pre-built tabular report (FR-1, FR-6) with server-side filters, sort and paging
    /// (FR-2, FR-3) and role scoping (BR-2). Returns the column headers + the requested page of rows.
    /// </summary>
    Task<Result<LeaveReportResult>> GenerateReportAsync(
        LeaveReportType reportType,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns aggregated, chart-library-agnostic data for a chart (FR-7) — utilization by department
    /// (AC-2), leave-by-type, or the 12-month trend by type (AC-4). Role-scoped (BR-2).
    /// </summary>
    Task<Result<LeaveAnalyticsResult>> GetAnalyticsAsync(
        LeaveAnalyticsChartType chartType,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a report to CSV or XLSX (FR-4, AC-5). The full (unpaged) row set is generated under the
    /// caller's role scope. For &lt;= 5,000 rows the file is generated synchronously and returned inline
    /// (NFR-2); for &gt; 5,000 rows a Hangfire background job is enqueued and a job id is returned
    /// (FR-5) — the job writes the file via the blob-storage seam and logs a notification.
    /// </summary>
    Task<Result<LeaveReportExportResult>> ExportReportAsync(
        LeaveReportType reportType,
        ReportExportFormat format,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the CSV/XLSX bytes for a fully-materialised report result. Used by both the
    /// synchronous export path and the Hangfire background-export job, so the two paths produce
    /// byte-identical files. Public so the job (in HRM.Api) can call it after re-running the report.
    /// </summary>
    (byte[] Content, string FileName, string ContentType) RenderExport(
        LeaveReportType reportType,
        ReportExportFormat format,
        LeaveReportResult result);

    /// <summary>
    /// Generates a report under a PRE-RESOLVED role scope (BR-2), bypassing ICurrentUser-based
    /// resolution. Used by the Hangfire background-export job, which has no HTTP-bound current user but
    /// must reproduce the requester's exact view. <paramref name="scopeKind"/> is "All"/"Manager"/
    /// "Employee"; <paramref name="scopeEmployeeId"/> is the acting employee for Manager/Employee scope.
    /// </summary>
    Task<Result<LeaveReportResult>> GenerateReportWithScopeAsync(
        LeaveReportType reportType,
        string scopeKind,
        Guid? scopeEmployeeId,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken = default);
}
