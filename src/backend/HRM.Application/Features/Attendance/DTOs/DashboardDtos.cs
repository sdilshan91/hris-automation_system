namespace HRM.Application.Features.Attendance.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-ATT-010 — Attendance Dashboard and Reports for HR.
//  Pinned API contract (frontend + QA build against this exact shape).
//  Base: /api/v1/attendance, ApiResponse<T> envelope, perm Attendance.View.All (HR);
//  team scope narrows via Attendance.Approve.Team (BR-4).
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Today's attendance KPIs for the HR dashboard (AC-1/FR-1, BR-1/BR-2/BR-3/BR-4).
/// expectedHeadcount = active employees − full-day approved leave − holiday-at-location (BR-1).
/// attendancePercent = clockedIn / expectedHeadcount * 100 (BR-2).
/// </summary>
public sealed record DashboardKpiDto
{
    /// <summary>The dashboard date, "yyyy-MM-dd".</summary>
    public string Date { get; init; } = string.Empty;

    public int ExpectedHeadcount { get; init; }
    public int ClockedIn { get; init; }
    public int PendingClockIn { get; init; }
    public int OnLeave { get; init; }
    public int Absent { get; init; }

    /// <summary>Live attendance percentage (clockedIn / expectedHeadcount * 100, 2dp).</summary>
    public decimal AttendancePercent { get; init; }
}

/// <summary>
/// One row in the live attendance board (AC-2/FR-2). status drives the status pill.
/// SignalR push is DEFERRED (US-NTF); the FE polls this endpoint (story §10 fallback).
/// </summary>
public sealed record LiveBoardRowDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeeNumber { get; init; }
    public string? DepartmentName { get; init; }

    /// <summary>"CLOCKED_IN" | "NOT_CLOCKED_IN" | "ON_LEAVE" | "HOLIDAY".</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Clock-in instant (ISO UTC) when CLOCKED_IN; null otherwise.</summary>
    public DateTime? ClockInAt { get; init; }
}

/// <summary>The live attendance board for a date (AC-2/FR-2).</summary>
public sealed record LiveBoardResult
{
    public string Date { get; init; } = string.Empty;
    public IReadOnlyList<LiveBoardRowDto> Rows { get; init; } = [];
}

/// <summary>One department's attendance rate for the month (AC-3/FR-3).</summary>
public sealed record DeptComparisonRowDto
{
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;

    /// <summary>Attendance rate % for the department (present / scheduled working days * 100, 2dp).</summary>
    public decimal AttendanceRatePct { get; init; }

    public int EmployeeCount { get; init; }
}

/// <summary>Department attendance comparison for a month (AC-3/FR-3).</summary>
public sealed record DeptComparisonResult
{
    /// <summary>The month, "yyyy-MM".</summary>
    public string Month { get; init; } = string.Empty;
    public IReadOnlyList<DeptComparisonRowDto> Rows { get; init; } = [];
}

/// <summary>One employee's rollup over a custom date range (AC-4/FR-4).</summary>
public sealed record CustomReportRowDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeeNumber { get; init; }
    public string? DepartmentName { get; init; }

    public decimal PresentDays { get; init; }
    public decimal AbsentDays { get; init; }
    public int LateCount { get; init; }
    public int OvertimeMinutes { get; init; }
    public int WorkMinutes { get; init; }
}

/// <summary>Custom date-range attendance report (AC-4/FR-4).</summary>
public sealed record CustomReportResult
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public IReadOnlyList<CustomReportRowDto> Rows { get; init; } = [];
}

/// <summary>Filter inputs for the custom report / export (AC-4/FR-4/FR-5).</summary>
public sealed record CustomReportFilter
{
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? ShiftId { get; init; }

    /// <summary>Optional attendance-status filter, e.g. "PRESENT"/"ABSENT"/"LATE".</summary>
    public string? Status { get; init; }
}

/// <summary>One point in a trend series (AC-5/FR-6/BR-5). period is "yyyy-MM".</summary>
public sealed record TrendPointDto
{
    public string Period { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

/// <summary>12-month trend analytics from the monthly summary table (AC-5/FR-6/BR-5).</summary>
public sealed record TrendsResult
{
    public IReadOnlyList<TrendPointDto> AttendanceRate { get; init; } = [];
    public IReadOnlyList<TrendPointDto> LateArrivals { get; init; } = [];
    public IReadOnlyList<TrendPointDto> OvertimeHours { get; init; } = [];
    public IReadOnlyList<TrendPointDto> AbsenteeismRate { get; init; } = [];
}

/// <summary>Result of a custom-report export (FR-5): the file bytes + name + content type.</summary>
public sealed record CustomReportExportResult
{
    public byte[] FileContent { get; init; } = [];
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public int RowCount { get; init; }
}

/// <summary>
/// A scheduled report configuration (§7, FR-8). The pinned contract DTO for the CRUD endpoints.
/// filters is a free-form object/dict; recipients are user GUIDs; deliveryTime is "HH:mm".
/// </summary>
public sealed record ScheduledReportConfigDto
{
    public Guid? Id { get; init; }

    /// <summary>Pre-built report type, e.g. "DEPARTMENT_COMPARISON" / "CUSTOM".</summary>
    public string ReportType { get; init; } = string.Empty;

    /// <summary>"DAILY" | "WEEKLY" | "MONTHLY".</summary>
    public string Frequency { get; init; } = string.Empty;

    /// <summary>Saved filter configuration (free-form object/dictionary).</summary>
    public Dictionary<string, object?> Filters { get; init; } = new();

    /// <summary>Recipient user IDs.</summary>
    public IReadOnlyList<Guid> Recipients { get; init; } = [];

    /// <summary>Time of day to send, "HH:mm" (24h).</summary>
    public string DeliveryTime { get; init; } = "08:00";

    /// <summary>"CSV" | "XLSX" | "PDF".</summary>
    public string Format { get; init; } = "CSV";

    public bool IsActive { get; init; } = true;
}
