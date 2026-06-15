using System.Globalization;

namespace HRM.Domain.Entities;

/// <summary>
/// Pure recruitment-analytics calculation helpers (US-REC-009). Extracted from the dashboard service so
/// the formulas (BR-1 time-to-hire, BR-2 offer acceptance, BR-3 funnel conversion) are unit-testable in
/// isolation, with no DbContext. All methods are deterministic and side-effect free.
/// </summary>
public static class RecruitmentAnalytics
{
    /// <summary>
    /// BR-3 funnel conversion: countCurrent / countPrevious * 100, rounded to 2 dp. Returns null when the
    /// previous stage had zero applicants (no meaningful conversion) or when there is no previous stage.
    /// </summary>
    public static decimal? ConversionRate(int countCurrent, int countPrevious)
    {
        if (countPrevious <= 0) return null;
        return Math.Round((decimal)countCurrent / countPrevious * 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// BR-2 offer acceptance: accepted / sent * 100, rounded to 2 dp. Returns 0 when no offers were sent.
    /// </summary>
    public static decimal AcceptanceRate(int accepted, int sent)
    {
        if (sent <= 0) return 0m;
        return Math.Round((decimal)accepted / sent * 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// BR-1 time-to-hire for a single applicant: whole calendar days from applied_at to the moment they
    /// entered the Hired stage. Negative spans (clock skew / bad data) are clamped to 0.
    /// </summary>
    public static int TimeToHireDays(DateTime appliedAt, DateTime hiredAt)
    {
        var days = (hiredAt.Date - appliedAt.Date).Days;
        return days < 0 ? 0 : days;
    }

    /// <summary>
    /// Mean of a set of time-to-hire day spans, rounded to 2 dp. Returns 0 for an empty set.
    /// </summary>
    public static decimal AverageDays(IReadOnlyCollection<int> daySpans)
    {
        if (daySpans.Count == 0) return 0m;
        return Math.Round((decimal)daySpans.Sum() / daySpans.Count, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Per-source hire conversion: hires / applicants * 100, rounded to 2 dp. Returns 0 for zero applicants.
    /// </summary>
    public static decimal SourceConversionRate(int hires, int applicants)
    {
        if (applicants <= 0) return 0m;
        return Math.Round((decimal)hires / applicants * 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The trend bucket label for a hire instant. "month" → "yyyy-MM"; anything else (default "week") →
    /// ISO-8601 week label "yyyy-Www" (e.g. "2026-W07"). Used by the FR-4 time-to-hire trend.
    /// </summary>
    public static string TrendBucket(DateTime instant, string granularity)
    {
        if (string.Equals(granularity, "month", StringComparison.OrdinalIgnoreCase))
            return instant.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var week = ISOWeek.GetWeekOfYear(instant);
        var year = ISOWeek.GetYear(instant);
        return $"{year:D4}-W{week:D2}";
    }
}
