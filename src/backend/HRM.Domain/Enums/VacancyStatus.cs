namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a job vacancy (US-REC-001 FR-2).
/// Stored as a string column for readability/forward-compat (see VacancyConfiguration).
/// </summary>
public enum VacancyStatus
{
    /// <summary>Work-in-progress, not yet published. Visible only internally to recruiters.</summary>
    Draft = 0,

    /// <summary>Published and accepting applications. Appears on internal + (if enabled) public listings.</summary>
    Open = 1,

    /// <summary>Temporarily paused; no new applications. Can return to Open.</summary>
    OnHold = 2,

    /// <summary>Filled or withdrawn; no new applications. Existing applicants keep their stage (BR-3).</summary>
    Closed = 3,

    /// <summary>Abandoned before/after publishing; terminal.</summary>
    Cancelled = 4,
}

/// <summary>
/// Allowed vacancy status transitions (US-REC-001 FR-2). Centralised so the service, validators,
/// and tests share one source of truth for what moves are legal.
/// </summary>
public static class VacancyStatusTransitions
{
    private static readonly IReadOnlyDictionary<VacancyStatus, VacancyStatus[]> _allowed =
        new Dictionary<VacancyStatus, VacancyStatus[]>
        {
            // Draft can be published (Open), or cancelled outright.
            [VacancyStatus.Draft] = [VacancyStatus.Open, VacancyStatus.Cancelled],
            // Open can be paused, closed (AC-5), or cancelled.
            [VacancyStatus.Open] = [VacancyStatus.OnHold, VacancyStatus.Closed, VacancyStatus.Cancelled],
            // On Hold can resume, be closed, or cancelled.
            [VacancyStatus.OnHold] = [VacancyStatus.Open, VacancyStatus.Closed, VacancyStatus.Cancelled],
            // Closed and Cancelled are terminal.
            [VacancyStatus.Closed] = [],
            [VacancyStatus.Cancelled] = [],
        };

    /// <summary>True if moving from <paramref name="from"/> to <paramref name="to"/> is a legal transition.</summary>
    public static bool IsAllowed(VacancyStatus from, VacancyStatus to)
        => from != to && _allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    /// <summary>The set of statuses reachable from <paramref name="from"/> (excludes itself).</summary>
    public static IReadOnlyList<VacancyStatus> AllowedFrom(VacancyStatus from)
        => _allowed.TryGetValue(from, out var targets) ? targets : [];
}
