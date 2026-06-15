namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a scheduled interview (US-REC-005 FR-6). A freshly scheduled interview starts at
/// <see cref="Scheduled"/>; the recruiter later marks it <see cref="Completed"/>, <see cref="Cancelled"/>
/// or <see cref="NoShow"/>. Only <see cref="Scheduled"/> interviews participate in conflict detection
/// (FR-7) and carry an active reminder job (FR-4). Stored as a string column for readability/forward-compat
/// (see InterviewConfiguration).
/// </summary>
public enum InterviewStatus
{
    /// <summary>The interview is scheduled and upcoming (FR-6). Default on creation.</summary>
    Scheduled = 0,

    /// <summary>The interview took place (FR-6).</summary>
    Completed = 1,

    /// <summary>The interview was cancelled (FR-6 / AC-3) — its reminder job is removed (BR-6).</summary>
    Cancelled = 2,

    /// <summary>A participant failed to attend (FR-6).</summary>
    NoShow = 3,
}
