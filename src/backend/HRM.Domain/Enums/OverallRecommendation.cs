namespace HRM.Domain.Enums;

/// <summary>
/// The interviewer's overall hiring recommendation on a scorecard (US-REC-006 FR-1/BR-3). Mandatory on
/// submit (BR-3) — it is the key advancement signal the recruiter reads. Ordered worst→best by numeric
/// value so it can drive simple comparisons. Serialized as its PascalCase member name on the wire (the API
/// uses a global JsonStringEnumConverter, US-PLT-003) and stored as a string column for readability
/// (see InterviewScorecardConfiguration).
/// </summary>
public enum OverallRecommendation
{
    /// <summary>Definitely should not hire.</summary>
    StrongNoHire = 0,

    /// <summary>Leaning against hiring.</summary>
    NoHire = 1,

    /// <summary>Leaning toward hiring.</summary>
    Hire = 2,

    /// <summary>Definitely should hire.</summary>
    StrongHire = 3,
}
