namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a performance goal (US-PRF-001 AC-4). Stored as a string column.
/// </summary>
public enum GoalStatus
{
    /// <summary>Created by the manager but not yet submitted to the employee.</summary>
    Draft = 0,

    /// <summary>Submitted to the employee for acknowledgement.</summary>
    Submitted = 1,

    /// <summary>
    /// Acknowledged by the employee. Once acknowledged, the manager can only modify with HR approval
    /// (BR-5 — the HR-approval flow itself is deferred to a later story; see the module note).
    /// </summary>
    Acknowledged = 2,

    /// <summary>
    /// BUG-056: the per-employee-per-cycle goal set has been finalized / locked after sign-off. A finalized
    /// set is immutable — create/update/delete/bulk-save are rejected (409 goals_finalized) until it is
    /// re-opened (re-open flow deferred / out of scope). Reached only when the set's weights sum to exactly
    /// 100%. Serialized as the string "Finalized" (fits the 20-char string status column — no migration).
    /// </summary>
    Finalized = 3,
}
