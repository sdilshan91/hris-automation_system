namespace HRM.Domain.Enums;

/// <summary>
/// The phases of an appraisal cycle (US-PRF-004 FR-1/FR-2). Phases are sequential and non-overlapping within
/// a cycle (BR-3). The GoalSetting / SelfAssessment / ManagerReview phases are the SOURCE OF TRUTH for the
/// open/closed window gates that US-PRF-001/002/003 consult (the cycle's legacy *Start/*End columns are kept
/// in sync with these phases on save). Calibration and Publish are optional (toggled per cycle, FR-6).
/// Stored as a string column.
/// </summary>
public enum CyclePhaseType
{
    /// <summary>Managers/HR set goals & KPIs (US-PRF-001 window).</summary>
    GoalSetting = 0,

    /// <summary>Employees self-rate against their goals (US-PRF-002 window).</summary>
    SelfAssessment = 1,

    /// <summary>Managers rate their direct reports (US-PRF-003 window).</summary>
    ManagerReview = 2,

    /// <summary>Optional calibration / normalization phase (FR-6 toggle).</summary>
    Calibration = 3,

    /// <summary>Final results are published to participants.</summary>
    Publish = 4,
}
