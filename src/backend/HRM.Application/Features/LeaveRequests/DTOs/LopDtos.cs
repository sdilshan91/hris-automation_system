namespace HRM.Application.Features.LeaveRequests.DTOs;

/// <summary>
/// Request body for POST /api/v1/leaves/assign-lop (US-LV-011 FR-3, AC-3). HR assigns LOP days to an
/// employee for the given dates with a reason.
/// </summary>
public sealed record AssignLopRequest
{
    public Guid EmployeeId { get; init; }
    /// <summary>The dates to mark as LOP. One leave_request row is created per date.</summary>
    public IReadOnlyList<DateOnly> Dates { get; init; } = [];
    public string? Reason { get; init; }
}

/// <summary>
/// Result of an HR LOP assignment (US-LV-011 AC-3). Reports how many LOP leave_request rows were created
/// and which dates were skipped (e.g. duplicates already marked LOP).
/// </summary>
public sealed record AssignLopResultDto
{
    public Guid EmployeeId { get; init; }
    public Guid LeaveTypeId { get; init; }
    public int CreatedCount { get; init; }
    public IReadOnlyList<DateOnly> SkippedDates { get; init; } = [];
    public IReadOnlyList<Guid> RequestIds { get; init; } = [];
}

/// <summary>
/// One LOP entry in the payroll-facing summary (US-LV-011 FR-5). Read-only.
/// </summary>
public sealed record LopEntryDto
{
    public Guid RequestId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal Days { get; init; }
    /// <summary>Origin of the LOP entry: EmployeeRequest / SystemGenerated / HrAssigned / Compulsory.</summary>
    public string Source { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

/// <summary>
/// LOP summary for an employee over a period, consumed by payroll (US-LV-011 FR-5, AC-4).
/// Payroll computes the salary deduction from <see cref="TotalLopDays"/>.
/// </summary>
public sealed record LopSummaryDto
{
    public Guid EmployeeId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    /// <summary>Total LOP days in the period (sum of <see cref="LopEntryDto.Days"/>).</summary>
    public decimal TotalLopDays { get; init; }
    public IReadOnlyList<LopEntryDto> Entries { get; init; } = [];
}

/// <summary>
/// Request body for POST /api/v1/leaves/compulsory (US-LV-011 FR-6). HR bulk-assigns a leave type to all
/// (or selected) employees for the given dates. BR-4: deduct from balance first, then LOP if insufficient.
/// </summary>
public sealed record CompulsoryLeaveRequest
{
    public Guid LeaveTypeId { get; init; }
    /// <summary>The shutdown dates to assign.</summary>
    public IReadOnlyList<DateOnly> Dates { get; init; } = [];
    /// <summary>
    /// Optional subset of employee ids. When null/empty, the leave is assigned to ALL active employees
    /// in the tenant ("Apply to all").
    /// </summary>
    public IReadOnlyList<Guid>? EmployeeIds { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Result of a compulsory-leave bulk assignment (US-LV-011 FR-6, BR-4).
/// </summary>
public sealed record CompulsoryLeaveResultDto
{
    public Guid LeaveTypeId { get; init; }
    public IReadOnlyList<DateOnly> Dates { get; init; } = [];
    public int EmployeesProcessed { get; init; }
    /// <summary>Total per-employee-per-date leave_request rows created.</summary>
    public int AssignedCount { get; init; }
    /// <summary>Of those, how many fell back to LOP for insufficient balance (BR-4).</summary>
    public int LopCount { get; init; }
}

/// <summary>
/// Request body for POST /api/v1/leaves/{id}/override-lop (US-LV-011 BR-3). HR converts a
/// system-generated LOP entry to a different leave type (with balance deduction) or removes it.
/// </summary>
public sealed record OverrideLopRequest
{
    /// <summary>
    /// Target leave type to convert the LOP entry to. When null, the LOP entry is removed (soft-cancelled).
    /// </summary>
    public Guid? TargetLeaveTypeId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Result of an LOP override (US-LV-011 BR-3).
/// </summary>
public sealed record OverrideLopResultDto
{
    public Guid RequestId { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsLop { get; init; }
    public Guid LeaveTypeId { get; init; }
    /// <summary>The id of the LeaveLedger "Used" deduction written when converting to a balance-backed type; null when removed or not deducted.</summary>
    public Guid? LedgerEntryId { get; init; }
}
