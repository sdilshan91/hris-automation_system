namespace HRM.Application.Features.LeaveReports.DTOs;

/// <summary>
/// The pre-built leave report types (US-LV-012 FR-1). Sent as the {reportType} path segment
/// (case-insensitive). The AC-named reports (BalanceSummary, Utilization, Absenteeism) plus the
/// FR-1 thin wrappers (CarryForwardSummary, LopSummary) are implemented; LeaveTrend is exposed via
/// the analytics endpoint (it is chart-shaped, AC-4). DepartmentCalendarCoverage is a documented stub.
/// </summary>
public enum LeaveReportType
{
    BalanceSummary,
    Utilization,
    Absenteeism,
    CarryForwardSummary,
    LopSummary,
    /// <summary>FR-1 "Department Leave Calendar Coverage" — STUBBED (returns empty + a TODO note).</summary>
    DepartmentCalendarCoverage,
}

/// <summary>
/// The analytics chart types for the chart-data API (US-LV-012 FR-7). Returned as chart-library-
/// agnostic series ({label, value} / multi-series), so the FE can feed any charting lib (NFR-4).
/// </summary>
public enum LeaveAnalyticsChartType
{
    /// <summary>Utilization % per department (AC-2) — bar/pie.</summary>
    UtilizationByDepartment,
    /// <summary>Total leave days taken per leave type — bar/pie.</summary>
    LeaveByType,
    /// <summary>Monthly leave totals by type over the past 12 months (AC-4) — line.</summary>
    MonthlyTrend,
}

/// <summary>
/// Export file format (US-LV-012 FR-4, AC-5).
/// </summary>
public enum ReportExportFormat
{
    Csv,
    Xlsx,
}

/// <summary>
/// Common filter + sort + paging parameters shared by all reports (US-LV-012 FR-2, FR-3).
/// Filters that don't apply to a given report are simply ignored by that report's query.
/// </summary>
public sealed record LeaveReportQueryParams
{
    /// <summary>Range start (inclusive). Applies to utilization/absenteeism/LOP reports.</summary>
    public DateOnly? From { get; init; }
    /// <summary>Range end (inclusive). Applies to utilization/absenteeism/LOP reports.</summary>
    public DateOnly? To { get; init; }
    /// <summary>Leave year for balance / carry-forward summaries (defaults to current calendar year).</summary>
    public int? Year { get; init; }
    /// <summary>Filter to a single department.</summary>
    public Guid? DepartmentId { get; init; }
    /// <summary>Filter to a single job title (proxy for "job level" — no JobLevel entity exists).</summary>
    public Guid? JobTitleId { get; init; }
    /// <summary>Filter to a single employment type (Full-Time / Part-Time / Contract / Intern).</summary>
    public string? EmploymentType { get; init; }
    /// <summary>Filter to a single leave type.</summary>
    public Guid? LeaveTypeId { get; init; }
    /// <summary>Free-text employee search (matches name or employee number, case-insensitive).</summary>
    public string? EmployeeSearch { get; init; }
    /// <summary>Sort field (report-specific; falls back to a sensible default per report).</summary>
    public string? SortBy { get; init; }
    /// <summary>Sort direction; true = ascending (default).</summary>
    public bool SortAscending { get; init; } = true;
    /// <summary>1-based page number (default 1).</summary>
    public int Page { get; init; } = 1;
    /// <summary>Page size (default 50, clamped [1, 500]).</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Server-side paged report envelope (US-LV-012 FR-3). Mirrors the established
/// PendingLeaveQueueResult / EmployeeListResult shape (Items + TotalCount + Page + PageSize) plus the
/// resolved column headers (for the generic table renderer and the export header row) and a scope echo.
/// </summary>
public sealed record LeaveReportResult
{
    /// <summary>The report type that was generated (echoed for the FE).</summary>
    public string ReportType { get; init; } = string.Empty;
    /// <summary>Ordered column headers for the report rows (drives the table + export header row).</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];
    /// <summary>The page of rows. Each row is an ordered list of cell values (string-formatted).</summary>
    public IReadOnlyList<LeaveReportRow> Rows { get; init; } = [];
    /// <summary>Total row count across all pages (before paging).</summary>
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    /// <summary>The role scope the data was computed under: "All" / "Manager" / "Employee" (BR-2).</summary>
    public string Scope { get; init; } = string.Empty;
    /// <summary>Optional note (e.g. a documented stub / deferral). Null for the implemented reports.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// One row in a report — ordered cell values aligned to <see cref="LeaveReportResult.Columns"/>.
/// Kept as strings so the same shape feeds the JSON table, the CSV writer, and the XLSX writer.
/// </summary>
public sealed record LeaveReportRow
{
    public IReadOnlyList<string> Cells { get; init; } = [];
}

/// <summary>
/// A single labelled data point for a chart series (US-LV-012 FR-7). Chart-library-agnostic.
/// </summary>
public sealed record ChartPoint
{
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

/// <summary>
/// A named series of <see cref="ChartPoint"/>s (e.g. one leave type across 12 months, AC-4).
/// </summary>
public sealed record ChartSeries
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ChartPoint> Points { get; init; } = [];
}

/// <summary>
/// The analytics payload for a chart (US-LV-012 FR-7). A single-series chart uses <see cref="Points"/>;
/// a multi-series chart (e.g. monthly trend per type) uses <see cref="Series"/> with shared
/// <see cref="Categories"/> (the X axis, e.g. month labels). Either may be empty.
/// </summary>
public sealed record LeaveAnalyticsResult
{
    public string ChartType { get; init; } = string.Empty;
    /// <summary>Single-series points (used by UtilizationByDepartment / LeaveByType).</summary>
    public IReadOnlyList<ChartPoint> Points { get; init; } = [];
    /// <summary>Shared X-axis category labels for multi-series charts (e.g. "2025-07" … "2026-06").</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
    /// <summary>Multi-series data (used by MonthlyTrend — one series per leave type).</summary>
    public IReadOnlyList<ChartSeries> Series { get; init; } = [];
    public string Scope { get; init; } = string.Empty;
}

/// <summary>
/// Result of an export request (US-LV-012 FR-4, FR-5, AC-5). For a synchronous export
/// (&lt;= 5,000 rows, NFR-2) <see cref="FileContent"/> + <see cref="FileName"/> + <see cref="ContentType"/>
/// are populated and <see cref="Queued"/> is false. For a large export (&gt; 5,000 rows) the work is
/// enqueued on Hangfire, <see cref="Queued"/> is true and <see cref="JobId"/> identifies the job;
/// file bytes are null (the file is written via the blob-storage seam, then a notification is logged).
/// </summary>
public sealed record LeaveReportExportResult
{
    public bool Queued { get; init; }
    public string? JobId { get; init; }
    public byte[]? FileContent { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    /// <summary>Total rows in the report (drives the sync-vs-background routing decision).</summary>
    public int RowCount { get; init; }
}
