namespace HRM.Domain.Payroll;

/// <summary>
/// Pure helpers for fiscal-year / effective-range resolution (US-PAY-006 FR-4). No DB / config — the caller
/// supplies the candidate rule versions and this picks the one in effect for a payroll period, so the
/// version-selection logic is unit-testable in isolation.
/// </summary>
public static class FiscalYearResolver
{
    /// <summary>
    /// Returns true when the pay-period date <paramref name="periodDate"/> falls within the rule's effective
    /// range [effectiveFrom, effectiveTo] (effectiveTo null = open-ended). Inclusive on both bounds.
    /// </summary>
    public static bool IsInEffect(DateOnly periodDate, DateOnly effectiveFrom, DateOnly? effectiveTo)
        => periodDate >= effectiveFrom && (effectiveTo is null || periodDate <= effectiveTo.Value);

    /// <summary>
    /// The representative date for a pay period — the first day of the pay month. Statutory rules effective
    /// on or before this date (and not yet expired) apply to the period.
    /// </summary>
    public static DateOnly PeriodDate(int payYear, int payMonth) => new(payYear, payMonth, 1);

    /// <summary>
    /// FR-4: from a set of candidate rule versions (each with an effective range), picks the one in effect
    /// for the period. When several overlap, the one with the latest <c>effectiveFrom</c> wins (the most
    /// recently configured version). Returns the selected index, or -1 when none is in effect.
    /// </summary>
    public static int SelectEffective(
        DateOnly periodDate, IReadOnlyList<(DateOnly EffectiveFrom, DateOnly? EffectiveTo)> candidates)
    {
        var best = -1;
        DateOnly bestFrom = default;
        for (var i = 0; i < candidates.Count; i++)
        {
            var (from, to) = candidates[i];
            if (!IsInEffect(periodDate, from, to))
                continue;
            if (best < 0 || from > bestFrom)
            {
                best = i;
                bestFrom = from;
            }
        }
        return best;
    }
}
