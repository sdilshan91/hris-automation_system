using HRM.Application.Features.Holidays.DTOs;

namespace HRM.Application.Features.LeaveRequests.DTOs;

/// <summary>
/// One leave entry on the team leave calendar (US-LV-009 FR-4).
///
/// Field population depends on the caller's scope (BR-1/BR-2/BR-3):
///   - Manager and HR scope: full detail — <see cref="LeaveTypeName"/>, <see cref="Color"/>,
///     and <see cref="Status"/> are populated (Approved AND Pending entries).
///   - Employee (department) scope: only Approved entries are returned and the leave-type
///     detail is SUPPRESSED — <see cref="LeaveTypeName"/>, <see cref="Color"/>, and
///     <see cref="Status"/> are null (BR-1: "just 'on leave'", no pending, no leave types).
/// The suppression is enforced server-side so the hidden fields never leave the API.
/// </summary>
public sealed record TeamLeaveCalendarEntryDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>Leave-type name; null for employee (department) scope per BR-1.</summary>
    public string? LeaveTypeName { get; init; }

    /// <summary>Hex colour of the leave type (e.g. "#4CAF50"); null for employee scope per BR-1.</summary>
    public string? Color { get; init; }

    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    /// <summary>"Approved" or "Pending"; null for employee scope per BR-1 (only approved shown).</summary>
    public string? Status { get; init; }

    public decimal TotalDays { get; init; }

    /// <summary>True when the entry is a half-day leave (BR-5, visual differentiation).</summary>
    public bool IsHalfDay { get; init; }

    /// <summary>"AM" or "PM" when <see cref="IsHalfDay"/> is true; otherwise null (BR-5).</summary>
    public string? HalfDaySession { get; init; }
}

/// <summary>
/// Response payload for the team leave calendar (US-LV-009 FR-1/FR-4/FR-7).
/// Carries the scoped leave entries plus the public holidays in the requested range
/// (FR-7, for background highlights) and the resolved scope so the client can adapt UI.
/// </summary>
public sealed record TeamLeaveCalendarDto
{
    /// <summary>The from-date (inclusive) the calendar was queried for.</summary>
    public DateOnly From { get; init; }

    /// <summary>The to-date (inclusive) the calendar was queried for.</summary>
    public DateOnly To { get; init; }

    /// <summary>
    /// Resolved access scope for the caller: "All" (Leave.View.All / HR),
    /// "Manager" (direct reports), or "Employee" (department colleagues, suppressed detail).
    /// </summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>The leave entries visible to the caller within the range.</summary>
    public IReadOnlyList<TeamLeaveCalendarEntryDto> Entries { get; init; } = [];

    /// <summary>Public holidays in the range, for background highlights (FR-7).</summary>
    public IReadOnlyList<HolidayDto> Holidays { get; init; } = [];
}

/// <summary>
/// Filter parameters for the team leave calendar (US-LV-009 FR-1/FR-6).
/// The status filter is manager/HR-only; it is ignored for employee scope (BR-1).
/// </summary>
public sealed record TeamLeaveCalendarQueryParams
{
    /// <summary>From-date (inclusive). Required.</summary>
    public DateOnly From { get; init; }

    /// <summary>To-date (inclusive). Required.</summary>
    public DateOnly To { get; init; }

    /// <summary>Optional FR-6 filter: only entries for this employee.</summary>
    public Guid? EmployeeId { get; init; }

    /// <summary>Optional FR-6 filter: only entries of this leave type (ignored for employee scope).</summary>
    public Guid? LeaveTypeId { get; init; }

    /// <summary>
    /// Optional FR-6 filter: "Approved" or "Pending". Manager/HR scope only — ignored for
    /// employee scope (which never sees pending).
    /// </summary>
    public string? Status { get; init; }
}
