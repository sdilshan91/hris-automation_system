using HRM.Application.Common.Interfaces;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Shared FR-6 slab-contiguity rule, used by the create + update validators and exercised directly in unit
/// tests. Tax slabs must form a contiguous, ascending partition of the income axis starting at 0: no gaps,
/// no overlaps, only the highest slab unbounded.
/// </summary>
public static class StatutorySlabRules
{
    public const string ContiguityMessage =
        "Tax slabs must be contiguous: start at 0, have no gaps or overlaps, and only the highest slab may be unbounded.";

    /// <summary>
    /// True when the slabs are contiguous (FR-6). Ordered by OrderIndex then SlabFrom; the first must start
    /// at 0; each slab's SlabTo must equal the next slab's SlabFrom (no gap, no overlap); only the last may
    /// have a null SlabTo. An empty set is treated as "not applicable" → valid (the NotEmpty rule handles
    /// the required case for income tax).
    /// </summary>
    public static bool AreContiguous(IReadOnlyList<TaxSlabInput>? slabs)
    {
        if (slabs is null || slabs.Count == 0)
            return true;

        var ordered = slabs.OrderBy(s => s.OrderIndex).ThenBy(s => s.SlabFrom).ToList();

        if (ordered[0].SlabFrom != 0m)
            return false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];

            // Each bounded slab's upper bound must exceed its lower bound.
            if (s.SlabTo is not null && s.SlabTo.Value <= s.SlabFrom)
                return false;

            var isLast = i == ordered.Count - 1;
            if (!isLast)
            {
                // A non-final slab must be bounded and meet the next slab exactly (no gap, no overlap).
                if (s.SlabTo is null)
                    return false;
                if (ordered[i + 1].SlabFrom != s.SlabTo.Value)
                    return false;
            }
        }

        return true;
    }
}
