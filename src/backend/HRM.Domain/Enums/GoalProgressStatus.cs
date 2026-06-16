namespace HRM.Domain.Enums;

/// <summary>
/// The progress status of a goal as captured on a <c>GoalProgressUpdate</c> (US-PRF-009 FR-2/FR-7). Rendered by
/// the FE as color-coded chips (§8): gray (NotStarted), blue (InProgress), green (Completed), amber (AtRisk),
/// red (Blocked). Stored as a string column. The status transitions are advisory (FR-7) — the employee posts the
/// status they choose on each append-only update; BR-2 auto-sets <see cref="Completed"/> when progress hits 100%
/// (overridable) and BR-3 (<see cref="Blocked"/>) triggers a manager + HR notification.
/// </summary>
public enum GoalProgressStatus
{
    /// <summary>No progress has been logged yet (gray).</summary>
    NotStarted = 0,

    /// <summary>The goal is actively being worked on (blue).</summary>
    InProgress = 1,

    /// <summary>The goal has been completed (green). BR-2: auto-set when progress reaches 100%.</summary>
    Completed = 2,

    /// <summary>The goal is at risk of not being met (amber).</summary>
    AtRisk = 3,

    /// <summary>The goal is blocked (red). BR-3: posting this notifies the manager + HR.</summary>
    Blocked = 4,
}
