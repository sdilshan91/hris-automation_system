namespace HRM.Domain.Enums;

/// <summary>
/// Structured reason an applicant was rejected (US-REC-004 AC-4/FR-3). When an applicant is moved to
/// <see cref="ApplicantStage.Rejected"/> a value from this enum is REQUIRED (in addition to the optional
/// free-text reason/notes); it backs the AC-4 rejection dropdown ("not qualified, position filled,
/// withdrew, other"). Stored as a string column for readability/forward-compat (see
/// ApplicantStageHistoryConfiguration).
/// </summary>
public enum RejectionReason
{
    /// <summary>The applicant did not meet the qualification / skills bar.</summary>
    NotQualified = 0,

    /// <summary>The position was filled by another candidate.</summary>
    PositionFilled = 1,

    /// <summary>The applicant withdrew from the process.</summary>
    Withdrew = 2,

    /// <summary>Any other reason — typically paired with free-text notes.</summary>
    Other = 3,
}
