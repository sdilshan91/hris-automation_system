namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a Performance Improvement Plan (US-PRF-008 FR-2). A PIP is created in
/// <see cref="Draft"/>, advances to <see cref="Active"/> once initiated, may be <see cref="Extended"/>, and
/// terminates in <see cref="SuccessfullyCompleted"/>, <see cref="NotMet"/> (which triggers escalation, AC-5) or
/// <see cref="Cancelled"/>. Stored as a string column.
/// </summary>
public enum PipStatus
{
    /// <summary>Created but not yet initiated. Default on first save (AC-1).</summary>
    Draft = 0,

    /// <summary>Initiated and in progress; employee, manager and mentor notified (AC-2).</summary>
    Active = 1,

    /// <summary>Duration extended with a new end date (and optional new objectives) (AC-4/FR-6).</summary>
    Extended = 2,

    /// <summary>Closed — employee met the objectives and returns to normal status (AC-4).</summary>
    SuccessfullyCompleted = 3,

    /// <summary>Closed — employee did not meet the objectives; triggers the configured escalation (AC-4/AC-5).</summary>
    NotMet = 4,

    /// <summary>Cancelled by HR before completion.</summary>
    Cancelled = 5,
}
