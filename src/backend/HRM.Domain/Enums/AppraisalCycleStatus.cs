namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of an appraisal cycle. MINIMAL placeholder for US-PRF-001 goal-setting; the full
/// cycle state machine (phases, transitions) is owned by US-PRF-004. Stored as a string column.
/// </summary>
public enum AppraisalCycleStatus
{
    /// <summary>Configured but not yet started; goal-setting not open.</summary>
    Draft = 0,

    /// <summary>Active cycle; the goal-setting window may be open (see the cycle's date range).</summary>
    Active = 1,

    /// <summary>Cycle closed; no further goal-setting.</summary>
    Closed = 2,
}
