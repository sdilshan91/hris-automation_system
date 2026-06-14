namespace HRM.Application.Features.Attendance.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-ATT-007 — Monthly attendance summary per employee DTOs.
//  Pinned API contract (frontend + QA build against this exact shape).
//  Base: /api/v1/attendance/summary, ApiResponse<T> envelope, perm Attendance.View.All (HR).
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// One per-employee summary row (US-ATT-007 AC-1/FR-3). present/absent/leave/lop are decimals to
/// support half-days (BR-5); minutes are integers (NFR-5 minute-accurate).
/// </summary>
public sealed record EmployeeMonthlySummaryDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeeNumber { get; init; }
    public string? DepartmentName { get; init; }

    /// <summary>Present days (decimal — 0.5 increments when half-day is enabled).</summary>
    public decimal PresentDays { get; init; }

    public decimal AbsentDays { get; init; }
    public int LateCount { get; init; }
    public int EarlyDepartureCount { get; init; }

    /// <summary>Total net worked minutes in the month.</summary>
    public int WorkMinutes { get; init; }

    /// <summary>Total APPROVED overtime minutes in the month.</summary>
    public int OvertimeMinutes { get; init; }

    public decimal LeaveDays { get; init; }
    public int Holidays { get; init; }
    public int WeeklyOffs { get; init; }
    public decimal LopDays { get; init; }

    /// <summary>When this employee's summary was last computed.</summary>
    public DateTime GeneratedAt { get; init; }
}

/// <summary>
/// Top-of-page banner aggregates for the monthly summary (US-ATT-007 §8).
/// </summary>
public sealed record MonthlySummaryBannerDto
{
    public int TotalEmployees { get; init; }

    /// <summary>Average attendance % across the listed employees (present / scheduled working days * 100).</summary>
    public decimal AverageAttendancePercent { get; init; }

    public decimal TotalLopDays { get; init; }
}

/// <summary>
/// The monthly summary result for the HR list view (US-ATT-007 AC-1/AC-5/FR-5).
/// <see cref="GeneratedAt"/> is null when no summary has been generated yet for the month (AC-3).
/// </summary>
public sealed record MonthlySummaryResult
{
    /// <summary>The summarized month, "yyyy-MM".</summary>
    public string YearMonth { get; init; } = string.Empty;

    public IReadOnlyList<EmployeeMonthlySummaryDto> Rows { get; init; } = [];

    public MonthlySummaryBannerDto Banner { get; init; } = new();

    /// <summary>The most recent generation timestamp across the rows; null when nothing is generated yet.</summary>
    public DateTime? GeneratedAt { get; init; }
}

/// <summary>
/// One day in an employee's day-by-day breakdown (US-ATT-007 AC-2).
/// </summary>
public sealed record DailyBreakdownDto
{
    /// <summary>The calendar date, "yyyy-MM-dd".</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>"PRESENT" | "ABSENT" | "LEAVE" | "HOLIDAY" | "WEEKLY_OFF" | "HALF_DAY".</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Clock-in instant (ISO UTC); null when there is no record that day.</summary>
    public DateTime? ClockIn { get; init; }

    /// <summary>Clock-out instant (ISO UTC); null when still open or no record.</summary>
    public DateTime? ClockOut { get; init; }

    /// <summary>Net worked minutes for the day; null when there is no record.</summary>
    public int? WorkMinutes { get; init; }

    public bool IsRegularized { get; init; }
    public bool IsLate { get; init; }
    public bool IsEarlyDeparture { get; init; }
}

/// <summary>
/// An employee's day-by-day attendance breakdown for a month (US-ATT-007 AC-2 drill-down).
/// </summary>
public sealed record EmployeeDailyBreakdownResult
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>The month, "yyyy-MM".</summary>
    public string YearMonth { get; init; } = string.Empty;

    public IReadOnlyList<DailyBreakdownDto> Days { get; init; } = [];
}

/// <summary>
/// Status of an on-demand summary generation request (US-ATT-007 AC-3/FR-4). The generation runs
/// synchronously (or via Hangfire) and the response reflects the resulting state.
/// </summary>
public sealed record SummaryGenerationStatusDto
{
    /// <summary>The month, "yyyy-MM".</summary>
    public string YearMonth { get; init; } = string.Empty;

    /// <summary>"PENDING" | "RUNNING" | "COMPLETED".</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>When the generation completed; null while pending/running.</summary>
    public DateTime? GeneratedAt { get; init; }
}

/// <summary>
/// Filter inputs shared by the list / export endpoints (US-ATT-007 FR-5/AC-5).
/// </summary>
public sealed record MonthlySummaryFilter
{
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? ShiftId { get; init; }

    /// <summary>Employment status filter, e.g. "Active"/"Probation"; null = all non-terminated.</summary>
    public string? Status { get; init; }
}

/// <summary>
/// Result of an export request (US-ATT-007 AC-4/FR-6). For the synchronous path the file bytes are
/// returned; for the large async path (&gt; 1,000 employees) the job is queued (FR-7).
/// </summary>
public sealed record MonthlySummaryExportResult
{
    public bool Queued { get; init; }
    public string? JobId { get; init; }
    public int RowCount { get; init; }

    public byte[]? FileContent { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
}
