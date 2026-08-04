namespace HRM.Domain.Enums;

/// <summary>
/// What an interview attachment IS (US-REC-005 FR-8). The AC names two document kinds explicitly — an
/// interview guide and an evaluation-criteria document — and a recruiter realistically attaches both, which
/// is why attachments are a child collection rather than a single column on the interview.
/// </summary>
public enum InterviewAttachmentKind
{
    /// <summary>The interview guide: questions, structure, timings.</summary>
    Guide = 0,

    /// <summary>The evaluation criteria / rubric the interviewer scores against.</summary>
    EvaluationCriteria = 1,

    /// <summary>Anything else the recruiter attaches for this interview.</summary>
    Other = 2,
}
