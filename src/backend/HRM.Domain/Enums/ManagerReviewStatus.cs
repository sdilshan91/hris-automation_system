namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a manager's performance review of an employee for one appraisal cycle
/// (US-PRF-003 AC-2/AC-5/BR-1). Stored as a string column.
/// </summary>
public enum ManagerReviewStatus
{
    /// <summary>In progress; partial manager ratings persisted via save-as-draft. Editable.</summary>
    Draft = 0,

    /// <summary>
    /// Submitted by the manager (AC-2). All goals rated with a comment ≥20 chars, weighted manager score
    /// and final combined score computed. Locked against further edits unless HR reopens it (AC-5/BR-3).
    /// </summary>
    Submitted = 1,
}
