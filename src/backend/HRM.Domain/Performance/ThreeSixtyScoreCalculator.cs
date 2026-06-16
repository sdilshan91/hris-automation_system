namespace HRM.Domain.Performance;

/// <summary>
/// Pure (no DB / no tenant context) 360-degree aggregation math (US-PRF-005 AC-4/FR-6). Computes the
/// COMPOSITE 360 score from per-category average ratings and the cycle's per-category weights, normalizing
/// by the weight of only the categories that actually have feedback so a missing perspective never skews
/// the result. Fully unit-testable in isolation; the same method feeds the final-score incorporation (BR-6).
/// </summary>
public static class ThreeSixtyScoreCalculator
{
    /// <summary>The four 360 per-category weights (whole percent). Only categories with feedback are used.</summary>
    public readonly record struct CategoryWeights(int Self, int Manager, int Peer, int Report);

    /// <summary>
    /// The average rating for each category that has at least one submitted feedback. A null entry means no
    /// feedback exists for that category (its weight is excluded from the composite normalization).
    /// </summary>
    public readonly record struct CategoryAverages(decimal? Self, decimal? Manager, decimal? Peer, decimal? Report);

    /// <summary>
    /// Computes the weighted 360 composite score (AC-4/FR-6). Each category's average is weighted by its
    /// configured weight; the sum is normalized by the total weight of the categories that actually have
    /// feedback. Returns 0 when no category has feedback. Result is rounded to 2 dp.
    /// </summary>
    public static decimal ComputeComposite(CategoryAverages averages, CategoryWeights weights)
    {
        decimal weightedSum = 0m;
        decimal totalWeight = 0m;

        Accumulate(averages.Self, weights.Self, ref weightedSum, ref totalWeight);
        Accumulate(averages.Manager, weights.Manager, ref weightedSum, ref totalWeight);
        Accumulate(averages.Peer, weights.Peer, ref weightedSum, ref totalWeight);
        Accumulate(averages.Report, weights.Report, ref weightedSum, ref totalWeight);

        return totalWeight == 0m
            ? 0m
            : Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero);
    }

    private static void Accumulate(decimal? avg, int weight, ref decimal weightedSum, ref decimal totalWeight)
    {
        if (avg is null || weight <= 0)
            return;
        weightedSum += avg.Value * weight;
        totalWeight += weight;
    }
}
