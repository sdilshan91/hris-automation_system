using Microsoft.Extensions.Logging;

namespace HRM.Application.Common.Helpers;

/// <summary>
/// Tenant-timezone-aware clock helpers (ISSUE-065 / Phase 2b). Attendance instants are always STORED in
/// UTC; only the <em>derived</em> values — which calendar day a punch belongs to (day-boundary), the
/// wall-clock time-of-day used for late/early comparison, and the "today" used for future-date / lookback
/// checks — are computed in the tenant's local zone.
///
/// Every method is a <b>pure</b> function of its inputs (a UTC instant / local date+time and a
/// <see cref="TimeZoneInfo"/>), which is what makes them unit-testable without a DbContext. Callers
/// resolve the tenant's <see cref="TimeZoneInfo"/> once per operation via <see cref="ResolveTimeZone"/>.
///
/// CRITICAL invariant: when the resolved zone is <see cref="TimeZoneInfo.Utc"/> (the tenant default, and
/// what the existing attendance test-suite uses), every conversion below is an exact no-op — the derived
/// values are byte-identical to the previous UTC-only behavior, so no existing attendance test changes.
/// </summary>
public static class TenantClock
{
    /// <summary>
    /// Resolves an IANA (e.g. <c>"America/New_York"</c>) or Windows time-zone id to a
    /// <see cref="TimeZoneInfo"/>. .NET 10 accepts IANA ids on all platforms via ICU. Falls back to
    /// <see cref="TimeZoneInfo.Utc"/> — never throws — when the id is null/blank or unrecognized
    /// (<see cref="TimeZoneNotFoundException"/> / <see cref="InvalidTimeZoneException"/>), logging a
    /// warning so a mis-configured tenant zone degrades gracefully instead of breaking clock-in.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger?.LogWarning(ex,
                "Unrecognized tenant time zone '{TimeZoneId}'; falling back to UTC for attendance calculations.",
                timeZoneId);
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>The tenant-local calendar day a UTC instant falls on (day-boundary derivation).</summary>
    public static DateOnly LocalDateOf(DateTime utcInstant, TimeZoneInfo tenantZone)
        => DateOnly.FromDateTime(ToLocal(utcInstant, tenantZone));

    /// <summary>The tenant-local wall-clock time-of-day of a UTC instant (late/early comparison).</summary>
    public static TimeOnly LocalTimeOfDay(DateTime utcInstant, TimeZoneInfo tenantZone)
        => TimeOnly.FromDateTime(ToLocal(utcInstant, tenantZone));

    /// <summary>The current tenant-local calendar date (used for future-date / lookback / cutoff checks).</summary>
    public static DateOnly TodayIn(TimeZoneInfo tenantZone)
        => DateOnly.FromDateTime(ToLocal(DateTime.UtcNow, tenantZone));

    /// <summary>
    /// Combines a tenant-local calendar date + wall-clock time into the UTC instant to store. For a UTC
    /// zone this equals <c>date.ToDateTime(time, DateTimeKind.Utc)</c> exactly (the prior behavior).
    /// </summary>
    public static DateTime LocalToUtc(DateOnly localDate, TimeOnly localTime, TimeZoneInfo tenantZone)
    {
        var local = DateTime.SpecifyKind(localDate.ToDateTime(localTime), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tenantZone);
    }

    private static DateTime ToLocal(DateTime utcInstant, TimeZoneInfo tenantZone)
        => TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc), tenantZone);
}
