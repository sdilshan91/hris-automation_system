namespace HRM.Application.Features.Attendance.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-ATT-006 — Overtime tracking and approval DTOs.
//  Pinned API contract (frontend + QA build against this exact shape).
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Request body for POST /api/v1/attendance/overtime/pre-approval (US-ATT-006 AC-2/FR-4).
/// The employee submits an intent to work overtime before starting.
/// </summary>
public sealed record OvertimePreApprovalRequest
{
    /// <summary>The date the overtime is expected, ISO yyyy-MM-dd.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Expected overtime hours (decimal, e.g. 2 or 2.5). Converted to minutes server-side.</summary>
    public decimal ExpectedHours { get; init; }

    /// <summary>Mandatory justification, minimum 10 characters.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Request body for POST /api/v1/attendance/overtime/{id}/approve (US-ATT-006 AC-4/FR-6).
/// </summary>
public sealed record ApproveOvertimeRequest
{
    /// <summary>
    /// Adjusted approved minutes (FR-6). Optional — defaults to the record's overtimeMinutes. Cannot
    /// exceed the recorded overtimeMinutes.
    /// </summary>
    public int? ApprovedMinutes { get; init; }

    /// <summary>Optional manager comment.</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// Request body for POST /api/v1/attendance/overtime/{id}/reject (US-ATT-006).
/// </summary>
public sealed record RejectOvertimeRequest
{
    /// <summary>Mandatory rejection reason, minimum 10 characters.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// An overtime record (US-ATT-006 §7). Returned to the employee (my list, pre-approval submit).
/// </summary>
public sealed record OvertimeDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }

    /// <summary>The attendance_log the overtime was detected from; null for a pre-approval request.</summary>
    public Guid? AttendanceLogId { get; init; }

    /// <summary>The overtime date (yyyy-MM-dd).</summary>
    public DateOnly Date { get; init; }

    public int OvertimeMinutes { get; init; }
    public int? ApprovedMinutes { get; init; }
    public decimal Multiplier { get; init; }

    /// <summary>"AUTO_DETECTED" | "PRE_APPROVED".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>"PENDING" | "APPROVED" | "REJECTED" | "UNAPPROVED".</summary>
    public string Status { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
    public string? ManagerComment { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// One row in a manager's pending overtime-approval queue (US-ATT-006 AC-3). OvertimeDto + queue
/// presentation fields (employee name/photo, submitted-on).
/// </summary>
public sealed record OvertimeQueueItemDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid? AttendanceLogId { get; init; }
    public DateOnly Date { get; init; }
    public int OvertimeMinutes { get; init; }
    public int? ApprovedMinutes { get; init; }
    public decimal Multiplier { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string? ManagerComment { get; init; }
    public DateTime CreatedAt { get; init; }

    // ── queue presentation ──
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeePhoto { get; init; }
    public DateTime SubmittedOn { get; init; }
}

/// <summary>
/// The manager's pending overtime-approval queue (US-ATT-006 AC-3).
/// </summary>
public sealed record OvertimeQueueResult
{
    public IReadOnlyList<OvertimeQueueItemDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
}

/// <summary>
/// Result of an approve/reject decision on an overtime record (US-ATT-006 AC-4).
/// </summary>
public sealed record OvertimeDecisionDto
{
    public Guid Id { get; init; }

    /// <summary>"APPROVED" | "REJECTED".</summary>
    public string Status { get; init; } = string.Empty;

    public int? ApprovedMinutes { get; init; }
    public decimal Multiplier { get; init; }
    public string? ManagerComment { get; init; }
    public DateTime ActionedAt { get; init; }
}

/// <summary>
/// Per-employee summary row in the monthly overtime report (US-ATT-006 AC-5).
/// </summary>
public sealed record OvertimeReportRowDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>Sum of approved_minutes for APPROVED records in the month.</summary>
    public int ApprovedMinutes { get; init; }

    /// <summary>Sum of overtime_minutes for PENDING records in the month.</summary>
    public int PendingMinutes { get; init; }

    /// <summary>Sum of overtime_minutes for REJECTED records in the month.</summary>
    public int RejectedMinutes { get; init; }

    /// <summary>Total overtime records for the employee in the month (all statuses).</summary>
    public int RecordCount { get; init; }
}

/// <summary>
/// Monthly overtime report (US-ATT-006 AC-5): per-employee approved/pending/rejected breakdown plus
/// tenant-wide totals for the selected month.
/// </summary>
public sealed record OvertimeReportResult
{
    /// <summary>The report month, "yyyy-MM".</summary>
    public string Month { get; init; } = string.Empty;

    public IReadOnlyList<OvertimeReportRowDto> Items { get; init; } = [];

    public OvertimeReportTotals Totals { get; init; } = new();
}

/// <summary>
/// Aggregate totals across all employees for the report month (US-ATT-006 AC-5).
/// </summary>
public sealed record OvertimeReportTotals
{
    public int ApprovedMinutes { get; init; }
    public int PendingMinutes { get; init; }
    public int RejectedMinutes { get; init; }
    public int RecordCount { get; init; }
}
