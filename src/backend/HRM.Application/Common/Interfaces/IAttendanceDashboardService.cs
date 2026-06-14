using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// HR attendance dashboard + reports service (US-ATT-010). Computes today's KPIs, the live board,
/// department comparison, custom date-range reports (+ export), 12-month trend analytics, and owns the
/// scheduled-report config CRUD. All reads aggregate the existing Attendance data (AttendanceLog,
/// OvertimeRecord, LeaveRequest, Holiday, Shift/EmployeeShift) and the AttendanceMonthlySummary table
/// (BR-5 trend source) — no new KPI/live-board/trend tables.
///
/// DEFERRALS (documented, do NOT block): SignalR live push (FR-2/NFR-2) — the live board is a plain GET
/// the FE polls (§10 fallback). Redis KPI cache (FR-7/NFR-1) — KPIs are computed from the DB. Email
/// delivery of scheduled reports (FR-8) — the Hangfire job generates the report but the send is
/// logged/no-op (US-NTF). PostgreSQL RLS (NFR-4) — EF global query filters + TenantInterceptor.
///
/// SCOPE (BR-3/BR-4): scope="all" requires HR (Attendance.View.All); scope="team" narrows to the acting
/// manager's direct reports (Attendance.Approve.Team). Enforced in the service.
/// </summary>
public interface IAttendanceDashboardService
{
    /// <summary>AC-1/FR-1: today's attendance KPIs for the date + scope (BR-1/BR-2/BR-3/BR-4).</summary>
    Task<Result<DashboardKpiDto>> GetKpisAsync(
        DateOnly date, string scope, CancellationToken cancellationToken = default);

    /// <summary>AC-2/FR-2: per-employee current status for the date (polling; SignalR deferred).</summary>
    Task<Result<LiveBoardResult>> GetLiveBoardAsync(
        DateOnly date, string scope, CancellationToken cancellationToken = default);

    /// <summary>AC-3/FR-3: attendance rate per department for a month.</summary>
    Task<Result<DeptComparisonResult>> GetDepartmentComparisonAsync(
        int year, int month, CancellationToken cancellationToken = default);

    /// <summary>AC-4/FR-4: per-employee rollup over a custom date range with filters.</summary>
    Task<Result<CustomReportResult>> GetCustomReportAsync(
        DateOnly from, DateOnly to, CustomReportFilter filter, CancellationToken cancellationToken = default);

    /// <summary>FR-5: renders the custom report as a CSV/XLSX/PDF file download.</summary>
    Task<Result<CustomReportExportResult>> ExportCustomReportAsync(
        DateOnly from, DateOnly to, string format, CustomReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>AC-5/FR-6/BR-5: 12-month trend series from the monthly summary table.</summary>
    Task<Result<TrendsResult>> GetTrendsAsync(int months, CancellationToken cancellationToken = default);

    // ── Scheduled report config CRUD (FR-8) ────────────────────────────
    Task<Result<IReadOnlyList<ScheduledReportConfigDto>>> GetScheduledReportsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ScheduledReportConfigDto>> CreateScheduledReportAsync(
        ScheduledReportConfigDto dto, CancellationToken cancellationToken = default);

    Task<Result<ScheduledReportConfigDto>> UpdateScheduledReportAsync(
        Guid id, ScheduledReportConfigDto dto, CancellationToken cancellationToken = default);

    Task<Result> DeleteScheduledReportAsync(Guid id, CancellationToken cancellationToken = default);
}
