namespace HRM.Domain.Performance;

/// <summary>
/// Pure goal-progress math for US-PRF-009 — the weighted overall-completion (FR-4) and the staleness gate
/// (AC-5/FR-6/BR-4). Kept framework-free in the Domain layer so both the service and the tests exercise the SAME
/// calculation (no duplicated arithmetic). Stateless.
/// </summary>
public static class GoalProgressCalculator
{
    /// <summary>
    /// The weighted overall completion % across an employee's goals (FR-4): the weighted average of each goal's
    /// latest progress %, by goal weight. Goals with no update count as 0% progress (they drag the average down,
    /// which is the intended "you haven't started" signal). When the total weight is 0 (defensive — weights should
    /// sum to 100 per US-PRF-001) it falls back to a simple arithmetic mean so a misconfigured set still yields a
    /// sensible number rather than a divide-by-zero. Returns a rounded whole percent in [0,100].
    /// </summary>
    /// <param name="goals">(weight, latestProgressPct) per goal. Empty ⇒ 0.</param>
    public static int WeightedOverallCompletion(IReadOnlyCollection<(int Weight, int LatestProgressPct)> goals)
    {
        if (goals.Count == 0)
            return 0;

        var totalWeight = goals.Sum(g => g.Weight);
        double result;
        if (totalWeight > 0)
        {
            var weightedSum = goals.Sum(g => (double)g.Weight * Clamp(g.LatestProgressPct));
            result = weightedSum / totalWeight;
        }
        else
        {
            result = goals.Average(g => (double)Clamp(g.LatestProgressPct));
        }

        return (int)Math.Round(Math.Clamp(result, 0, 100), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// True when a goal is STALE — its most recent activity is older than <paramref name="nudgeDays"/> as of
    /// <paramref name="nowUtc"/> (AC-5/FR-6). A goal with NO update yet is measured from <paramref name="sinceUtc"/>
    /// (the cycle's goal-setting close / goal creation). When <paramref name="nudgeDays"/> is ≤ 0 the sweep is
    /// disabled and nothing is ever stale (BR-4).
    /// </summary>
    public static bool IsStale(DateTime? lastUpdatedUtc, DateTime sinceUtc, int nudgeDays, DateTime nowUtc)
    {
        if (nudgeDays <= 0)
            return false;

        var reference = lastUpdatedUtc ?? sinceUtc;
        return (nowUtc - reference).TotalDays >= nudgeDays;
    }

    private static int Clamp(int pct) => Math.Clamp(pct, 0, 100);
}
