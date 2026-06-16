namespace HRM.Domain.Enums;

/// <summary>
/// Sign-off lifecycle of a manager review's meeting-notes &amp; digital sign-off workflow (US-PRF-006).
/// Distinct from <see cref="ManagerReviewStatus"/> (which tracks the rating workflow): a review must already be
/// <see cref="ManagerReviewStatus.Submitted"/> (BR-1) before notes/sign-off begin. Stored as a string column.
/// </summary>
public enum ReviewSignoffStatus
{
    /// <summary>No meeting notes yet — the sign-off workflow has not started. Default for a submitted review.</summary>
    NotStarted = 0,

    /// <summary>Meeting notes drafted/saved by the manager but employee sign-off not yet requested (AC-1).</summary>
    NotesAdded = 1,

    /// <summary>Employee sign-off requested; awaiting the employee's Acknowledge &amp; Sign / Dispute (AC-2).</summary>
    PendingEmployeeSignOff = 2,

    /// <summary>Employee acknowledged &amp; signed; the review is finalized and LOCKED (AC-3/BR-5).</summary>
    SignedOff = 3,

    /// <summary>Employee disputed with mandatory comments; escalated to HR until resolved (AC-3/FR-5/BR-4).</summary>
    Disputed = 4,

    /// <summary>Employee did not sign within the configurable window; auto-closed by the Hangfire job (BR-3).</summary>
    NoResponse = 5,
}
