namespace HRM.Domain.Recruitment;

/// <summary>
/// The Phase-1 fixed set of interview evaluation criteria (US-REC-006 FR-1/BR-2). Per-tenant configurable
/// criteria (S35.2.9) are DEFERRED — in Phase 1 every tenant uses this shared default list. A criterion is
/// identified on the wire by its stable <see cref="ScorecardCriterion.Key"/> (a tenant-agnostic slug); the
/// <see cref="ScorecardCriterion.Label"/> is the human display name. Ratings are validated against this set
/// (an unknown key is rejected) so stored ratings always reference a known criterion.
/// </summary>
public static class ScorecardCriteria
{
    /// <summary>The inclusive 1-5 rating scale bounds (FR-1, fixed in Phase 1).</summary>
    public const int MinScore = 1;
    public const int MaxScore = 5;

    /// <summary>The default, ordered criteria set (BR-2). Order is the recommended form display order.</summary>
    public static readonly IReadOnlyList<ScorecardCriterion> Default = new[]
    {
        new ScorecardCriterion("technical_skills", "Technical Skills"),
        new ScorecardCriterion("communication", "Communication"),
        new ScorecardCriterion("problem_solving", "Problem Solving"),
        new ScorecardCriterion("cultural_fit", "Cultural Fit"),
    };

    private static readonly HashSet<string> DefaultKeys =
        Default.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>True when <paramref name="key"/> is one of the default criterion keys (case-sensitive).</summary>
    public static bool IsValidKey(string? key) => key is not null && DefaultKeys.Contains(key);
}

/// <summary>A single evaluation criterion: a stable wire <paramref name="Key"/> + a display <paramref name="Label"/>.</summary>
public sealed record ScorecardCriterion(string Key, string Label);
