namespace HRM.Domain.Enums;

/// <summary>
/// The action captured by an immutable <see cref="HRM.Domain.Performance.PipEvent"/> entry (US-PRF-008 FR-5).
/// The append-only event log is the compliance audit trail of the PIP. Stored as a string column.
/// </summary>
public enum PipEventType
{
    /// <summary>The PIP was created (Draft, AC-1).</summary>
    Created = 0,

    /// <summary>The PIP was initiated (Draft → Active); employee/manager/mentor notified (AC-2).</summary>
    Initiated = 1,

    /// <summary>The employee acknowledged the PIP (BR-4).</summary>
    Acknowledged = 2,

    /// <summary>The system flagged the PIP as not acknowledged within 5 business days (BR-4).</summary>
    NotAcknowledged = 3,

    /// <summary>A checkpoint assessment was recorded (AC-3/FR-4).</summary>
    CheckpointRecorded = 4,

    /// <summary>The PIP was extended with a new end date (and optional new objectives) (AC-4/FR-6).</summary>
    Extended = 5,

    /// <summary>The final outcome was set to Successfully Completed (AC-4).</summary>
    CompletedSuccessfully = 6,

    /// <summary>The final outcome was set to Not Met (AC-4) — escalation pending HR confirmation.</summary>
    MarkedNotMet = 7,

    /// <summary>HR confirmed the escalation decision for a Not-Met PIP (AC-5/BR-6).</summary>
    EscalationConfirmed = 8,

    /// <summary>The PIP was cancelled by HR (FR-2).</summary>
    Cancelled = 9,
}
