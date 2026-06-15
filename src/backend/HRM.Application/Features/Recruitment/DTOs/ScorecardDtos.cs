using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

/// <summary>
/// Service-layer input for submitting (or editing) a scorecard (US-REC-006 FR-1/FR-2). Carried by
/// <c>SubmitScorecardCommand</c>. The interviewer is resolved from the auth context (BR-1), not the input.
/// </summary>
public sealed record SubmitScorecardInput
{
    public Guid InterviewId { get; init; }
    public OverallRecommendation OverallRecommendation { get; init; }
    public string? GeneralNotes { get; init; }

    /// <summary>The per-criterion ratings (FR-1, ≥1 required; each key from the default criteria set).</summary>
    public IReadOnlyList<CriterionRatingInput> Ratings { get; init; } = [];
}

/// <summary>A single criterion rating on submit (US-REC-006 FR-1).</summary>
public sealed record CriterionRatingInput
{
    public string CriterionKey { get; init; } = string.Empty;
    public int Score { get; init; }
    public string? Comment { get; init; }
}

/// <summary>A single criterion rating returned to the FE (US-REC-006 AC-2). camelCase on the wire.</summary>
public sealed record CriterionRatingDto
{
    public string CriterionKey { get; init; } = string.Empty;

    /// <summary>Display label for the criterion (resolved from the default set), e.g. "Technical Skills".</summary>
    public string CriterionLabel { get; init; } = string.Empty;

    public int Score { get; init; }
    public string? Comment { get; init; }
}

/// <summary>
/// One interviewer's scorecard returned to the FE (US-REC-006 AC-2/AC-3). camelCase on the wire. Carries
/// the per-criterion ratings, the scorecard's own average (FR-3), the overall recommendation, and lock
/// state. Anti-bias filtering (FR-6/BR-5) happens before this is built — a caller who may not see a given
/// interviewer's scorecard never receives it.
/// </summary>
public sealed record ScorecardDto
{
    public Guid Id { get; init; }
    public Guid InterviewId { get; init; }
    public Guid InterviewerEmployeeId { get; init; }
    public string InterviewerName { get; init; } = string.Empty;

    public OverallRecommendation OverallRecommendation { get; init; }
    public string OverallRecommendationName { get; init; } = string.Empty;

    /// <summary>Mean of this scorecard's criterion ratings (FR-3), rounded to 2 decimals.</summary>
    public decimal AverageScore { get; init; }

    public string? GeneralNotes { get; init; }

    public IReadOnlyList<CriterionRatingDto> Ratings { get; init; } = [];

    public DateTime SubmittedAt { get; init; }
    public DateTime LockedAt { get; init; }

    /// <summary>True when the lock period has passed (BR-4) — the scorecard is now immutable.</summary>
    public bool IsLocked { get; init; }
}

/// <summary>
/// The consolidated scorecards view for an interview or applicant (US-REC-006 AC-2/AC-3). camelCase on the
/// wire. <see cref="Scorecards"/> are the per-interviewer cards the caller is allowed to see (anti-bias
/// filtered, FR-6). <see cref="AggregateAverageScore"/> is the mean across the VISIBLE scorecards' averages
/// (FR-3); null when none are visible. <see cref="HiddenCount"/> tells the FE how many scorecards exist but
/// are hidden from this caller (so it can show the "submit yours first" overlay).
/// </summary>
public sealed record ScorecardSummaryDto
{
    public IReadOnlyList<ScorecardDto> Scorecards { get; init; } = [];

    /// <summary>Mean across the visible scorecards' averages (FR-3); null when there are none.</summary>
    public decimal? AggregateAverageScore { get; init; }

    /// <summary>Number of submitted scorecards hidden from this caller by the anti-bias rule (FR-6/BR-5).</summary>
    public int HiddenCount { get; init; }

    /// <summary>
    /// True when the caller is an assigned interviewer who has NOT yet submitted their own scorecard, so the
    /// anti-bias overlay applies (FR-6). False for recruiters (who see everything) and for interviewers who
    /// have already submitted.
    /// </summary>
    public bool AntiBiasApplies { get; init; }
}

/// <summary>A single evaluation criterion returned by the criteria endpoint (US-REC-006 FR-1/BR-2).</summary>
public sealed record ScorecardCriterionDto
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}
