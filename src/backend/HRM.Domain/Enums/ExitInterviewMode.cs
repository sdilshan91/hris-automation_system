namespace HRM.Domain.Enums;

/// <summary>
/// How an exit interview was conducted (US-ONB-006 FR-2, §7). Stored as a string column.
/// </summary>
public enum ExitInterviewMode
{
    /// <summary>HR officer fills in the responses on the departing employee's behalf (FR-2).</summary>
    HrConducted = 0,

    /// <summary>The departing employee completes the questionnaire themselves (FR-2, AC-3).</summary>
    SelfService = 1,
}
