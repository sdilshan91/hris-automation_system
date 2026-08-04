using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// An immutable snapshot of an <see cref="InterviewScorecard"/> as it stood BEFORE an edit (US-REC-006 AC-K1).
///
/// <para><b>The problem this solves.</b> Editing a scorecard used to replace its rating set wholesale — the old
/// <see cref="ScorecardCriterionRating"/> rows were deleted and new ones inserted. Combined with
/// <see cref="InterviewScorecard"/> being <c>IAuditExempt</c>, that meant an edit silently rewrote what a
/// historical scorecard MEANT, with nothing anywhere recording the prior judgement. A hiring decision could be
/// reviewed months later against scores that were never the ones it was made on.</para>
///
/// <para><b>Why a snapshot table rather than versioning the live rows.</b> Versioning the ratings in place would
/// force every read of <c>Ratings</c> to filter by version — touching every consumer for the benefit of a path
/// that is only ever read during a review. Appending a snapshot is purely additive: current-state reads are
/// untouched, and the history is queried only when somebody asks for it.</para>
///
/// <para><b>What this deliberately is NOT.</b> AC-K1 also speaks of versioning scorecard <i>templates</i>, but
/// <c>ScorecardCriteria</c> is a hard-coded static list — there is no tenant-configurable template entity to
/// version. That half needs a template feature built first and is re-filed as its own scoped story rather than
/// smuggled in here as an unstated prerequisite.</para>
/// </summary>
public sealed class InterviewScorecardRevision : BaseEntity
{
    /// <summary>The scorecard this snapshot belongs to.</summary>
    public Guid ScorecardId { get; set; }

    /// <summary>
    /// The version number this snapshot CAPTURES — i.e. the value <see cref="InterviewScorecard.Version"/> held
    /// before the edit that produced it. Version 1 is the original submission.
    /// </summary>
    public int Version { get; set; }

    /// <summary>The overall recommendation as it stood at this version.</summary>
    public OverallRecommendation OverallRecommendation { get; set; }

    /// <summary>The mean criterion score as it stood at this version.</summary>
    public decimal AverageScore { get; set; }

    /// <summary>The free-text notes as they stood at this version.</summary>
    public string? GeneralNotes { get; set; }

    /// <summary>
    /// The criterion ratings as they stood at this version, serialized as JSON.
    ///
    /// <para>Stored as a document rather than child rows because a revision is only ever read as a whole — it is
    /// a historical record, never queried by criterion or aggregated across revisions. Child rows would add a
    /// table and a join for no read this feature performs.</para>
    /// </summary>
    public string RatingsJson { get; set; } = "[]";

    /// <summary>When the edit that superseded this version happened.</summary>
    public DateTime RevisedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The interviewer who made the edit. Same person as the scorecard's owner today (only the assigned
    /// interviewer may submit or edit), but recorded explicitly so the trail survives any future change to
    /// who is allowed to edit.
    /// </summary>
    public Guid RevisedByEmployeeId { get; set; }

    // Navigation
    public InterviewScorecard? Scorecard { get; set; }
}
