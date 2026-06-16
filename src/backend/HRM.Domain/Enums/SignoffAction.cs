namespace HRM.Domain.Enums;

/// <summary>
/// The action captured by an immutable <c>ReviewSignoff</c> entry (US-PRF-006 FR-3/FR-4/FR-7). The
/// append-only sign-off log is the audit trail of the workflow. Stored as a string column.
/// </summary>
public enum SignoffAction
{
    /// <summary>The manager requested employee sign-off (their own acknowledgement that notes are ready, AC-2).</summary>
    RequestedSignOff = 0,

    /// <summary>The employee acknowledged &amp; signed — agreement with the assessment (AC-3/FR-4).</summary>
    Acknowledged = 1,

    /// <summary>The employee disputed the assessment with mandatory comments (AC-3/FR-4/FR-5).</summary>
    Disputed = 2,

    /// <summary>HR confirmed the review as-is after a dispute (BR-4).</summary>
    DisputeConfirmed = 3,

    /// <summary>HR amended/resolved the review after a dispute (BR-4).</summary>
    DisputeAmended = 4,

    /// <summary>The system auto-closed the review for no employee response within the window (BR-3).</summary>
    AutoClosedNoResponse = 5,
}
