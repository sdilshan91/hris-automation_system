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
/// stays pure and deterministic.
/// </summary>
public static class OvertimeMultiplierResolver
{
    /// <summary>
    /// Returns the multiplier and its basis for <paramref name="date"/>:
    /// public holiday > weekend (Sat/Sun) > weekday. Holiday and weekend rates may differ (BR-7).
    /// </summary>
    public static (decimal Multiplier, string Basis) Resolve(
        DateOnly date,
        bool isPublicHoliday,
        decimal weekdayMultiplier,
        decimal weekendMultiplier,
        decimal holidayMultiplier)
    {
        if (isPublicHoliday)
            return (holidayMultiplier, OvertimeMultiplierBasis.Holiday);

        var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        return isWeekend
            ? (weekendMultiplier, OvertimeMultiplierBasis.Weekend)
            : (weekdayMultiplier, OvertimeMultiplierBasis.Weekday);
    }
}
