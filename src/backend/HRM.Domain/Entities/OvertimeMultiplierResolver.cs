namespace HRM.Domain.Entities;

/// <summary>
/// The basis on which an overtime multiplier was selected (US-ATT-006 BR-3/BR-7), recorded in the
/// audit string (NFR-3).
/// </summary>
public static class OvertimeMultiplierBasis
{
    public const string Weekday = "WEEKDAY";
    public const string Weekend = "WEEKEND";
    public const string Holiday = "HOLIDAY";
}

/// <summary>
/// Pure resolution of the applicable overtime multiplier for a date (US-ATT-006 BR-3/BR-7). Lives in
/// the Domain layer (no framework deps). The holiday determination (whether the date is a public
/// holiday) is made by the caller against the tenant holiday calendar and passed in as a flag so this
/// stays pure and deterministic — and, since BUG-285, the employee's working week is supplied the same
/// way: the caller resolves it (US-ATT-011's four-tier chain) and passes the set in.
/// </summary>
public static class OvertimeMultiplierResolver
{
    /// <summary>
    /// Returns the multiplier and its basis for <paramref name="date"/>:
    /// public holiday &gt; weekend &gt; weekday. Holiday and weekend rates may differ (BR-7).
    /// </summary>
    /// <param name="workingDays">
    /// BUG-285 / US-ATT-011 AC-2: the employee's RESOLVED working-weekday set, ISO 1=Mon..7=Sun. "Weekend"
    /// means <i>not a working day for this employee</i> — it is NOT a hardcoded Sat/Sun. A Gulf employee on
    /// Sun–Thu must have FRIDAY paid at the weekend multiplier and SUNDAY at the weekday multiplier; before
    /// this parameter existed, both were exactly inverted and the wrong multiplier flowed straight into
    /// payroll earnings.
    /// <para>Null or empty falls back to the legacy Sat/Sun basis. That is a defensive backstop for callers
    /// that cannot resolve a week, NOT a supported path: <c>ShiftScheduleResolver</c> never returns an empty
    /// set (its Mon–Fri code default and <c>ToCalendar</c> guarantee that), so a production caller always
    /// passes a real week.</para>
    /// </param>
    public static (decimal Multiplier, string Basis) Resolve(
        DateOnly date,
        bool isPublicHoliday,
        decimal weekdayMultiplier,
        decimal weekendMultiplier,
        decimal holidayMultiplier,
        IReadOnlySet<int>? workingDays = null)
    {
        if (isPublicHoliday)
            return (holidayMultiplier, OvertimeMultiplierBasis.Holiday);

        var isWeekend = workingDays is { Count: > 0 }
            ? !workingDays.Contains(IsoDay(date))
            : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        return isWeekend
            ? (weekendMultiplier, OvertimeMultiplierBasis.Weekend)
            : (weekdayMultiplier, OvertimeMultiplierBasis.Weekday);
    }

    /// <summary>
    /// ISO day-of-week (1=Mon..7=Sun) for <paramref name="date"/>, matching <c>Shift.WorkingDays</c> and
    /// <c>ShiftScheduleResolver</c>. .NET's <see cref="DayOfWeek"/> is Sun=0..Sat=6, so Sunday maps 0 → 7.
    /// </summary>
    private static int IsoDay(DateOnly date)
    {
        var dow = (int)date.DayOfWeek;   // Sun=0..Sat=6
        return dow == 0 ? 7 : dow;       // 1=Mon..7=Sun
    }
}
