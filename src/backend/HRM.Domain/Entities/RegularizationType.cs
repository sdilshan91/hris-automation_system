namespace HRM.Domain.Entities;

/// <summary>
/// The allowed values for <see cref="AttendanceRegularization.RegularizationType"/> and
/// <see cref="AttendanceRegularization.Status"/> (US-ATT-003 §7). Stored as varchar(20); these
/// constants keep the magic strings in one place.
/// </summary>
public static class RegularizationType
{
    public const string MissedClockIn = "MISSED_CLOCK_IN";
    public const string MissedClockOut = "MISSED_CLOCK_OUT";
    public const string MissedBoth = "MISSED_BOTH";

    public static readonly IReadOnlyList<string> All = new[] { MissedClockIn, MissedClockOut, MissedBoth };

    /// <summary>True when the type implies a corrected clock-in is required.</summary>
    public static bool RequiresClockIn(string type) => type is MissedClockIn or MissedBoth;

    /// <summary>True when the type implies a corrected clock-out is required.</summary>
    public static bool RequiresClockOut(string type) => type is MissedClockOut or MissedBoth;
}

/// <summary>
/// The allowed values for <see cref="AttendanceRegularization.Status"/> (US-ATT-003 §7).
/// </summary>
public static class RegularizationStatus
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";
}
