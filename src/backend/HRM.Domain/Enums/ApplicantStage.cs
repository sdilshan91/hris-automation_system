namespace HRM.Domain.Enums;

/// <summary>
/// Recruitment pipeline stage of an applicant (US-REC-002 FR-6).
/// Only <see cref="Applied"/> is set/used by US-REC-002 (initial stage on submission); the later
/// stages exist so the enum and storage column are forward-compatible with the pipeline-management
/// stories (US-REC-003+). Stored as a string column for readability (see ApplicantConfiguration).
/// </summary>
public enum ApplicantStage
{
    /// <summary>Initial stage on submission (FR-6). The only stage produced by US-REC-002.</summary>
    Applied = 0,

    /// <summary>Under CV/screening review (later stories).</summary>
    Screening = 1,

    /// <summary>In the interview process (later stories).</summary>
    Interview = 2,

    /// <summary>An offer has been extended (later stories).</summary>
    Offer = 3,

    /// <summary>Hired (later stories).</summary>
    Hired = 4,

    /// <summary>Rejected at any stage (later stories).</summary>
    Rejected = 5,
}
