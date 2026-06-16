namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of an appraisal cycle (US-PRF-004 FR-7). Stored as a string column.
///
/// The legacy <see cref="Closed"/> value (US-PRF-001) is retained for backward compatibility but is
/// superseded by <see cref="Completed"/> in the full state machine. The valid transitions (FR-7) are:
/// Draft → Active → Paused → Active → Completed, and Draft → Cancelled / Active → Cancelled / Paused → Cancelled.
/// </summary>
public enum AppraisalCycleStatus
{
    /// <summary>Configured but not yet started; goal-setting not open. Editable; deletable.</summary>
    Draft = 0,

    /// <summary>Active cycle; the current phase window may be open. Rating scale is locked (BR-5).</summary>
    Active = 1,

    /// <summary>
    /// Legacy "closed" state from the minimal US-PRF-001 cycle. Retained so existing data/tests stay valid;
    /// the full state machine uses <see cref="Completed"/>. Treated as terminal.
    /// </summary>
    Closed = 2,

    /// <summary>Temporarily halted (US-PRF-004 FR-7). All phase windows treated as closed; can resume to Active.</summary>
    Paused = 3,

    /// <summary>All phases finished and results published (US-PRF-004 FR-7). Terminal.</summary>
    Completed = 4,

    /// <summary>Cancelled with a recorded reason (US-PRF-004 FR-7/BR-6). Terminal; participants notified.</summary>
    Cancelled = 5,
}
