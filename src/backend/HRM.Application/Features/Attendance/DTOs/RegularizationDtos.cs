using System.Globalization;

namespace HRM.Application.Features.Attendance.DTOs;

/// <summary>
/// Request body for POST /api/v1/attendance/regularizations (US-ATT-003 §7).
/// The corrected times are sent as wall-clock <c>HH:mm</c> strings (24-hour) paired with the separate
/// <see cref="Date"/>; the backend combines date + time into the stored UTC instant.
/// TODO(tenant-timezone): the wall-clock is treated as UTC for now — no tenant-timezone infrastructure
/// exists yet (consistent with how US-ATT-001/002 deferred tenant-tz day-boundary semantics). When that
/// lands, combine in the tenant's zone and convert to UTC here. The times are conditional on
/// <see cref="RegularizationType"/>.
/// </summary>
public sealed record SubmitRegularizationRequest
{
    /// <summary>The calendar date to regularize (FR-1/§7), ISO yyyy-MM-dd.</summary>
    public DateOnly Date { get; init; }

    /// <summary>"MISSED_CLOCK_IN" | "MISSED_CLOCK_OUT" | "MISSED_BOTH" (§7).</summary>
    public string RegularizationType { get; init; } = default!;

    /// <summary>Corrected clock-in wall-clock time, "HH:mm" (24h). Required for MISSED_CLOCK_IN / MISSED_BOTH.</summary>
    public string? RequestedClockIn { get; init; }

    /// <summary>Corrected clock-out wall-clock time, "HH:mm" (24h). Required for MISSED_CLOCK_OUT / MISSED_BOTH.</summary>
    public string? RequestedClockOut { get; init; }

    /// <summary>Mandatory justification, minimum 10 characters (BR-7).</summary>
    public string Reason { get; init; } = default!;

    /// <summary>
    /// Parses a wall-clock "HH:mm" (24h) string into a <see cref="TimeOnly"/>. Returns false for null,
    /// whitespace, or any other format. Shared by the validator (shape check) and the service (so both
    /// agree on what a valid time is). Only the exact "HH:mm" pattern is accepted.
    /// </summary>
    public static bool TryParseTime(string? value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return TimeOnly.TryParseExact(
            value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    /// <summary>
    /// Combines <see cref="Date"/> with a parsed "HH:mm" wall-clock time into a UTC instant.
    /// Returns null when <paramref name="value"/> is null/blank/invalid (the caller decides whether a
    /// missing/invalid time is an error per regularization type). TODO(tenant-timezone): treats the
    /// wall-clock as UTC; convert from the tenant zone once that infrastructure exists.
    /// </summary>
    public DateTime? CombineToUtc(string? value)
        => TryParseTime(value, out var time)
            ? Date.ToDateTime(time, DateTimeKind.Utc)
            : null;
}

/// <summary>
/// DTO for an attendance regularization record (US-ATT-003). Returned on submit (AC-1/AC-2) and in
/// the employee's regularization list (drives the attendance-history status pills, §8).
/// </summary>
public sealed record RegularizationDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }

    /// <summary>The linked existing attendance log, when one existed for the date (AC-2/BR-5); else null.</summary>
    public Guid? AttendanceLogId { get; init; }

    /// <summary>The regularized calendar date (yyyy-MM-dd).</summary>
    public DateOnly Date { get; init; }

    /// <summary>"MISSED_CLOCK_IN" | "MISSED_CLOCK_OUT" | "MISSED_BOTH".</summary>
    public string RegularizationType { get; init; } = default!;

    /// <summary>Requested clock-in instant (UTC); the UI converts to local (FR-7). Null when N/A.</summary>
    public DateTime? RequestedClockIn { get; init; }

    /// <summary>Requested clock-out instant (UTC); the UI converts to local (FR-7). Null when N/A.</summary>
    public DateTime? RequestedClockOut { get; init; }

    public string Reason { get; init; } = default!;

    /// <summary>"PENDING" | "APPROVED" | "REJECTED" | "CANCELLED" — the status pill (§8).</summary>
    public string Status { get; init; } = "PENDING";

    public DateTime CreatedAt { get; init; }
}
