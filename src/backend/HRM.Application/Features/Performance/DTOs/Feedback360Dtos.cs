using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.DTOs;

// ── Reviewer configuration / nomination (AC-1/FR-1/FR-2) ───────────────────

/// <summary>One row in the reviewer-configuration view (US-PRF-005 AC-1): an assigned or suggested reviewer.</summary>
public sealed record ReviewerAssignmentDto
{
    public Guid Id { get; init; }
    public Guid CycleId { get; init; }
    public Guid RevieweeEmployeeId { get; init; }
    public Guid ReviewerEmployeeId { get; init; }
    public string ReviewerName { get; init; } = string.Empty;
    public string? ReviewerEmployeeNo { get; init; }
    public ReviewerCategory Category { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public ReviewerAssignmentStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateTime? NotifiedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}

/// <summary>The full reviewer-configuration view for one reviewee + cycle (US-PRF-005 AC-1).</summary>
public sealed record ReviewerConfigurationDto
{
    public Guid CycleId { get; init; }
    public Guid RevieweeEmployeeId { get; init; }
    public string RevieweeName { get; init; } = string.Empty;
    public bool Is360Enabled { get; init; }
    public bool IsAnonymousFeedback { get; init; }
    public int MinPeerReviewers { get; init; }
    /// <summary>Currently-persisted reviewer assignments grouped is up to the FE; flat list here.</summary>
    public IReadOnlyList<ReviewerAssignmentDto> Assignments { get; init; } = [];
    /// <summary>Suggested-but-not-yet-assigned peers (same department) the HR/manager can add (AC-1/FR-2).</summary>
    public IReadOnlyList<SuggestedReviewerDto> SuggestedPeers { get; init; } = [];
    /// <summary>Suggested-but-not-yet-assigned direct reports the HR/manager can add (AC-1/FR-2).</summary>
    public IReadOnlyList<SuggestedReviewerDto> SuggestedDirectReports { get; init; } = [];
}

/// <summary>A suggested (not yet assigned) reviewer candidate (US-PRF-005 AC-1).</summary>
public sealed record SuggestedReviewerDto
{
    public Guid EmployeeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? EmployeeNo { get; init; }
}

// ── Feedback submission (AC-3/FR-4) ────────────────────────────────────────

/// <summary>One competency/goal rating in a feedback submission (US-PRF-005 FR-4).</summary>
public sealed record Feedback360ItemInput
{
    public Guid? GoalId { get; init; }
    public string? CompetencyKey { get; init; }
    public int Rating { get; init; }
    public string? Comment { get; init; }
}

/// <summary>Service-layer input for submitting a 360 feedback (US-PRF-005 AC-3).</summary>
public sealed record SubmitFeedback360Input(
    Guid CycleId,
    Guid RevieweeEmployeeId,
    string? OverallComment,
    IReadOnlyList<Feedback360ItemInput> Items);

// ── Request bodies ─────────────────────────────────────────────────────────

/// <summary>
/// Add-reviewer request (US-PRF-005 AC-1/FR-2). The reviewee + cycle ride the route; the body carries the
/// reviewer and category.
/// </summary>
public sealed record AddReviewerRequest(Guid ReviewerEmployeeId, ReviewerCategory Category);

/// <summary>
/// Submit-feedback request (US-PRF-005 AC-3). The reviewee + cycle ride the route; the reviewer is resolved
/// from the authenticated caller (never supplied — NFR-2/NFR-3).
/// </summary>
public sealed record SubmitFeedback360Request(
    string? OverallComment,
    IReadOnlyList<Feedback360ItemInput> Items);

// ── Aggregated results (AC-4/FR-6) ─────────────────────────────────────────

/// <summary>
/// One submitted feedback as shown in the results view (US-PRF-005 AC-4). When anonymity is on the reviewer
/// identity fields (<see cref="ReviewerEmployeeId"/> / <see cref="ReviewerName"/>) are NULL — enforced in
/// the projection (NFR-3/FR-5), not just hidden in the UI.
/// </summary>
public sealed record Feedback360ResultEntryDto
{
    public Guid FeedbackId { get; init; }
    public ReviewerCategory Category { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public bool IsAnonymous { get; init; }
    /// <summary>NULL when the feedback was submitted anonymously (NFR-3/FR-5).</summary>
    public Guid? ReviewerEmployeeId { get; init; }
    /// <summary>NULL when the feedback was submitted anonymously (NFR-3/FR-5).</summary>
    public string? ReviewerName { get; init; }
    public string? OverallComment { get; init; }
    public DateTime SubmittedAt { get; init; }
    public IReadOnlyList<Feedback360ResultItemDto> Items { get; init; } = [];
}

/// <summary>One competency/goal rating within a result entry (US-PRF-005 AC-4).</summary>
public sealed record Feedback360ResultItemDto
{
    public Guid? GoalId { get; init; }
    public string? CompetencyKey { get; init; }
    public string Label { get; init; } = string.Empty;
    public int Rating { get; init; }
    public string? Comment { get; init; }
}

/// <summary>Average rating for one competency across all submitted feedback (US-PRF-005 AC-4).</summary>
public sealed record CompetencyAverageDto
{
    public Guid? GoalId { get; init; }
    public string? CompetencyKey { get; init; }
    public string Label { get; init; } = string.Empty;
    public decimal AverageRating { get; init; }
    public int ResponseCount { get; init; }
}

/// <summary>Per-category breakdown for the radar chart (US-PRF-005 AC-4 — self/manager/peer/report).</summary>
public sealed record CategoryAverageDto
{
    public ReviewerCategory Category { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal? AverageRating { get; init; }
    public int ResponseCount { get; init; }
    public int Weight { get; init; }
}

/// <summary>The completion tracker per category (US-PRF-005 AC-3/AC-4).</summary>
public sealed record CategoryCompletionDto
{
    public ReviewerCategory Category { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public int Assigned { get; init; }
    public int Completed { get; init; }
}

/// <summary>
/// The full aggregated 360 results for one reviewee + cycle (US-PRF-005 AC-4/FR-6). Carries the composite
/// score (FR-6), the per-competency and per-category averages (radar chart), the completion tracker, the
/// individual (anonymized when applicable) entries, and the BR-4 minimum-peer release gate.
/// </summary>
public sealed record Feedback360ResultsDto
{
    public Guid CycleId { get; init; }
    public Guid RevieweeEmployeeId { get; init; }
    public string RevieweeName { get; init; } = string.Empty;
    public bool IsAnonymousFeedback { get; init; }
    public int RatingScaleMax { get; init; }

    /// <summary>The weighted 360 composite score (FR-6).</summary>
    public decimal CompositeScore { get; init; }

    public IReadOnlyList<CompetencyAverageDto> CompetencyAverages { get; init; } = [];
    public IReadOnlyList<CategoryAverageDto> CategoryAverages { get; init; } = [];
    public IReadOnlyList<CategoryCompletionDto> Completion { get; init; } = [];
    public IReadOnlyList<Feedback360ResultEntryDto> Entries { get; init; } = [];

    // BR-4: minimum-peer release gate.
    public int MinPeerReviewers { get; init; }
    public int PeerResponseCount { get; init; }
    /// <summary>True when at least <see cref="MinPeerReviewers"/> peers have submitted (BR-4).</summary>
    public bool MinPeerThresholdMet { get; init; }
    /// <summary>Non-null warning string when the peer threshold is not met (BR-4 — HR is warned, not blocked).</summary>
    public string? ReleaseWarning { get; init; }
}
