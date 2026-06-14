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
/// Current clock-in status for the authenticated employee (US-ATT-001, drives the dashboard widget).
/// Returned by GET /api/v1/attendance/status. The shift fields are placeholders until US-ATT-005
/// introduces a shift entity and are always null today; the UI renders a placeholder for nulls.
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
}
