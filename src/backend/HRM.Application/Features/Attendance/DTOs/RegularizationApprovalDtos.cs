namespace HRM.Application.Features.Attendance.DTOs;

/// <summary>
/// Request body for POST /api/v1/attendance/regularizations/{id}/approve (US-ATT-004 FR-2/BR-2).
/// Comment is optional on approval.
/// </summary>
public sealed record ApproveRegularizationRequest
{
    /// <summary>Optional approval comment shown to the employee (BR-2).</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// Request body for POST /api/v1/attendance/regularizations/{id}/reject (US-ATT-004 FR-3/BR-1).
/// Reason is mandatory, minimum 10 characters.
/// </summary>
public sealed record RejectRegularizationRequest
{
    /// <summary>Mandatory rejection reason, minimum 10 characters (BR-1, AC-2).</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Request body for POST /api/v1/attendance/regularizations/bulk-approve (US-ATT-004 BR-7).
/// Approves multiple regularizations in one action; each item is independently authorized and
/// payroll-lock-checked, and per-item results are returned.
/// </summary>
public sealed record BulkApproveRegularizationRequest
{
    /// <summary>The regularization ids to approve.</summary>
    public IReadOnlyList<Guid> RegularizationIds { get; init; } = [];

    /// <summary>Optional approval comment applied to every item (BR-2).</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// Result of a single approve/reject decision on a regularization (US-ATT-004 AC-1/AC-2).
/// On approval the affected attendance_log id and its recalculated totals are surfaced so the FE can
/// render the corrected day without a re-fetch.
/// </summary>
public sealed record RegularizationDecisionDto
{
    public Guid RegularizationId { get; init; }

    /// <summary>New regularization status: "APPROVED" or "REJECTED".</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The action recorded in history: "APPROVED" or "REJECTED".</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Approval level the decision was recorded at (always 1 in this story).</summary>
    public int ApprovalLevel { get; init; }

    /// <summary>
    /// The attendance_log created or updated on approval (FR-2); null on rejection.
    /// </summary>
    public Guid? AttendanceLogId { get; init; }

    /// <summary>Recomputed net worked minutes on the affected log (FR-2); null on rejection.</summary>
    public int? TotalWorkMinutes { get; init; }

    /// <summary>Recomputed overtime minutes on the affected log; null on rejection.</summary>
    public int? OvertimeMinutes { get; init; }

    /// <summary>Recomputed log status (COMPLETE/SHORT_DAY/OVERTIME/ANOMALY); null on rejection.</summary>
    public string? AttendanceStatus { get; init; }

    /// <summary>The mandatory rejection reason / optional approval comment recorded (FR-6).</summary>
    public string? Comment { get; init; }

    /// <summary>When the decision was taken (FR-6).</summary>
    public DateTime ActionedAt { get; init; }
}

/// <summary>
/// One row in a manager's pending regularization-approval queue (US-ATT-004 FR-1/AC-3).
/// Projected directly from the join to avoid N+1 (NFR-1).
/// </summary>
public sealed record PendingRegularizationDto
{
    public Guid RegularizationId { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeePhoto { get; init; }

    /// <summary>The calendar date being regularized (AC-3).</summary>
    public DateOnly Date { get; init; }

    /// <summary>"MISSED_CLOCK_IN" | "MISSED_CLOCK_OUT" | "MISSED_BOTH".</summary>
    public string RegularizationType { get; init; } = default!;

    /// <summary>Requested corrected clock-in instant (UTC); null when N/A (AC-3).</summary>
    public DateTime? RequestedClockIn { get; init; }

    /// <summary>Requested corrected clock-out instant (UTC); null when N/A (AC-3).</summary>
    public DateTime? RequestedClockOut { get; init; }

    /// <summary>The employee's justification (AC-3).</summary>
    public string Reason { get; init; } = default!;

    /// <summary>When the request was submitted (AC-3 "Submitted On").</summary>
    public DateTime SubmittedOn { get; init; }
}

/// <summary>
/// The manager's pending-approval queue (US-ATT-004 FR-1/AC-3).
/// </summary>
public sealed record PendingRegularizationQueueResult
{
    public IReadOnlyList<PendingRegularizationDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
}

/// <summary>
/// Filter parameters for the manager's pending regularization queue (US-ATT-004 FR-1).
/// </summary>
public sealed record PendingRegularizationQueryParams
{
    /// <summary>Optional filter: only requests from this direct report.</summary>
    public Guid? EmployeeId { get; init; }

    /// <summary>Optional filter: only requests whose regularized date is on/after this date.</summary>
    public DateOnly? FromDate { get; init; }

    /// <summary>Optional filter: only requests whose regularized date is on/before this date.</summary>
    public DateOnly? ToDate { get; init; }
}

/// <summary>
/// One item result inside a bulk-approve response (US-ATT-004 BR-7).
/// Either <see cref="Decision"/> (on success) or <see cref="Error"/> + <see cref="ErrorCode"/>
/// (on failure) is populated for each requested id.
/// </summary>
public sealed record BulkRegularizationItemResult
{
    public Guid RegularizationId { get; init; }
    public bool Succeeded { get; init; }
    public RegularizationDecisionDto? Decision { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Aggregate result of a bulk-approve action (US-ATT-004 BR-7). Per-item results let the FE show a
/// partial-success summary (e.g. "5 approved, 1 blocked: locked payroll period").
/// </summary>
public sealed record BulkApproveRegularizationResult
{
    public int TotalRequested { get; init; }
    public int SucceededCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<BulkRegularizationItemResult> Items { get; init; } = [];
}
