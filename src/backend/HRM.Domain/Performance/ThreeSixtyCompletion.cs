using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// Pure (no DB / no tenant context) 360-degree completion counting (US-PRF-005 AC-3/AC-4). Given a set of
/// <see cref="ReviewerAssignment"/> rows for one reviewee + cycle, produces the per-category assigned/completed
/// tallies that drive BOTH the aggregated results tracker (<c>Feedback360Service</c>) and the standalone
/// completion tracker (<c>ReviewerAssignmentService</c>, BUG-244 #3) — the one place this math lives so the two
/// surfaces can never drift. Fully unit-testable in isolation.
/// </summary>
public static class ThreeSixtyCompletion
{
    /// <summary>Per-category tally: how many reviewers are assigned and how many have submitted (Completed).</summary>
    public readonly record struct CategoryCount(ReviewerCategory Category, int Assigned, int Completed);

    /// <summary>
    /// One <see cref="CategoryCount"/> for every <see cref="ReviewerCategory"/> (always all four, in enum order),
    /// counting the supplied assignments. <c>Completed</c> counts assignments whose status is
    /// <see cref="ReviewerAssignmentStatus.Completed"/>.
    /// </summary>
    public static IReadOnlyList<CategoryCount> ByCategory(IEnumerable<ReviewerAssignment> assignments)
    {
        var rows = assignments as IReadOnlyCollection<ReviewerAssignment> ?? assignments.ToList();
        return Enum.GetValues<ReviewerCategory>()
            .Select(cat => new CategoryCount(
                cat,
                rows.Count(a => a.Category == cat),
                rows.Count(a => a.Category == cat && a.Status == ReviewerAssignmentStatus.Completed)))
            .ToList();
    }
}
