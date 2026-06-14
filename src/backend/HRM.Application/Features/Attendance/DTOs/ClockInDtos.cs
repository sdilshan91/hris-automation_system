namespace HRM.Application.Features.Attendance.DTOs;

/// <summary>
/// Request body for POST /api/v1/attendance/clock-in (US-ATT-001 §7).
/// Coordinates and the selfie photo are conditional on the tenant's attendance policy.
/// </summary>
public sealed record ClockInRequest
{
    /// <summary>Captured latitude (required when tenant geo policy is mandatory, AC-3).</summary>
    public decimal? Latitude { get; init; }

    /// <summary>Captured longitude (required when tenant geo policy is mandatory, AC-3).</summary>
    public decimal? Longitude { get; init; }

    /// <summary>
    /// Already-uploaded selfie photo URL (required when tenant photo policy is enabled, BR-6).
    /// Blob upload itself is out of scope; only the resolved URL is accepted here.
    /// </summary>
    public string? PhotoUrl { get; init; }

    /// <summary>Punch origin: "WEB" (default) or "MOBILE_WEB" (§7).</summary>
    public string? Source { get; init; }
}

/// <summary>
/// Service-layer input for a clock-in (US-ATT-001). Carries the request-body fields plus the
/// HTTP-context-derived audit fields (IP, user agent) and idempotency key resolved by the
/// controller, since the service has no HttpContext access.
/// </summary>
public sealed record ClockInData
{
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? PhotoUrl { get; init; }
    public string? Source { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// DTO returned for a successful clock-in (US-ATT-001 AC-1).
/// </summary>
public sealed record AttendanceLogDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    /// <summary>Clock-in instant in UTC; the UI converts to the employee's local timezone (FR-7).</summary>
    public DateTime ClockIn { get; init; }
    public DateTime? ClockOut { get; init; }
    public decimal? ClockInLatitude { get; init; }
    public decimal? ClockInLongitude { get; init; }
    public string Source { get; init; } = "WEB";
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Request body for POST /api/v1/attendance/clock-out (US-ATT-002 §7).
/// Coordinates are conditional on the tenant's geolocation policy (AC-5 / FR-6).
/// </summary>
public sealed record ClockOutRequest
{
    /// <summary>Captured latitude at clock-out (required when tenant geo policy is mandatory, AC-5).</summary>
    public decimal? Latitude { get; init; }

    /// <summary>Captured longitude at clock-out (required when tenant geo policy is mandatory, AC-5).</summary>
    public decimal? Longitude { get; init; }
}

/// <summary>
/// Service-layer input for a clock-out (US-ATT-002). Carries the request-body coordinates plus the
/// HTTP-context-derived audit fields (IP, user agent) resolved by the controller, since the service
/// has no HttpContext access. Mirrors <see cref="ClockInData"/>.
/// </summary>
public sealed record ClockOutData
{
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}

/// <summary>
/// DTO returned for a successful clock-out (US-ATT-002 AC-1/AC-3/AC-4). Carries the computed
/// session summary the dashboard renders: in/out instants (UTC), net worked minutes, overtime,
/// and the outcome status pill.
/// </summary>
public sealed record ClockOutResultDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }

    /// <summary>Clock-in instant in UTC; the UI converts to the employee's local timezone (FR-7/NFR-5).</summary>
    public DateTime ClockIn { get; init; }

    /// <summary>Clock-out instant in UTC (FR-1). Always set on a successful clock-out.</summary>
    public DateTime ClockOut { get; init; }

    /// <summary>Net worked minutes after auto-break deduction (FR-2/FR-3, BR-2).</summary>
    public int TotalWorkMinutes { get; init; }

    /// <summary>Overtime minutes beyond standard+threshold (FR-4, BR-3); 0 when none.</summary>
    public int OvertimeMinutes { get; init; }

    /// <summary>"COMPLETE" | "SHORT_DAY" | "OVERTIME" | "ANOMALY" (§7).</summary>
    public string Status { get; init; } = "COMPLETE";
}

/// <summary>
/// Current clock-in status for the authenticated employee (US-ATT-001/US-ATT-002, drives the
/// dashboard widget). Returned by GET /api/v1/attendance/status. The shift fields are placeholders
/// until US-ATT-005 introduces a shift entity and are always null today; the UI renders a
/// placeholder for nulls.
/// </summary>
public sealed record ClockStatusDto
{
    /// <summary>True when the employee has an open (un-clocked-out) attendance record (BR-1).</summary>
    public bool IsClockedIn { get; init; }

    /// <summary>
    /// Clock-in instant (UTC) of the open record, or null when not clocked in. The UI converts to
    /// the employee's local timezone (FR-7).
    /// </summary>
    public DateTime? ClockedInAt { get; init; }

    /// <summary>
    /// BR-2 flag from the tenant's attendance settings; drives the UI's AC-3 (required) vs AC-4
    /// (optional) geolocation branch. Defaults to false when no settings row exists.
    /// </summary>
    public bool RequireGeolocation { get; init; }

    /// <summary>Shift name — always null until US-ATT-005 introduces shifts.</summary>
    public string? ShiftName { get; init; }

    /// <summary>Shift start — always null until US-ATT-005 introduces shifts.</summary>
    public DateTime? ShiftStart { get; init; }

    /// <summary>
    /// US-ATT-002: summary of the employee's most recently CLOSED session, so the dashboard can
    /// render the post-clock-out summary card after a reload. Null when the employee has no closed
    /// session yet. Independent of <see cref="IsClockedIn"/> (an employee may be clocked in again
    /// after a prior close).
    /// </summary>
    public ClockOutResultDto? LastCompleted { get; init; }
}
