namespace HRM.Application.Features.LeaveRequests;

/// <summary>
/// Pure working-day calculation for leave requests (US-LV-003 FR-3).
///
/// Excludes weekends (configurable work-week, 5-day Mon-Fri default) and public holidays.
/// Static and pure — no I/O, no DI — for easy unit testing (mirrors LeaveEntitlementEngine).
/// </summary>
public static class WorkingDaysCalculator
{
    /// <summary>
    /// Default 5-day work week: Monday through Friday are working days.
    /// </summary>
    public static readonly IReadOnlySet<DayOfWeek> DefaultWorkWeek = new HashSet<DayOfWeek>
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
    };

    /// <summary>
    /// Counts working days in the inclusive range [start, end], excluding non-working
    /// week days and the supplied public holidays.
    /// </summary>
    /// <param name="start">First day of leave (inclusive).</param>
    /// <param name="end">Last day of leave (inclusive). Must be &gt;= start.</param>
    /// <param name="workWeek">Set of week days that count as working days. Defaults to Mon-Fri.</param>
    /// <param name="holidays">Public holidays to exclude. Defaults to none.</param>
    /// <returns>Whole-day working count (use the half-day path for 0.5).</returns>
    public static decimal CountWorkingDays(
        DateOnly start,
        DateOnly end,
        IReadOnlySet<DayOfWeek>? workWeek = null,
        IReadOnlySet<DateOnly>? holidays = null)
    {
        if (end < start)
            return 0m;

        var week = workWeek is { Count: > 0 } ? workWeek : DefaultWorkWeek;
        var hols = holidays ?? EmptyHolidays;

        int count = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (!week.Contains(day.DayOfWeek))
                continue;
            if (hols.Contains(day))
                continue;
            count++;
        }

        return count;
    }

    private static readonly IReadOnlySet<DateOnly> EmptyHolidays = new HashSet<DateOnly>();
}
