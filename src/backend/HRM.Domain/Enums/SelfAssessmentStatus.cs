namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of an employee self-assessment (US-PRF-002 AC-2/AC-3/BR-3). Stored as a string column.
/// </summary>
public enum SelfAssessmentStatus
{
    /// <summary>In progress; partial ratings persisted via save-as-draft (AC-3/FR-6). Editable.</summary>
    Draft = 0,

    /// <summary>
    /// Submitted by the employee (AC-2). All goals rated, comments ≥20 chars, weighted self-score computed.
    /// Locked against further edits unless a manager/HR reopens it back to Draft (BR-3 — see ReopenAsync).
    /// </summary>
    Submitted = 1,
}
