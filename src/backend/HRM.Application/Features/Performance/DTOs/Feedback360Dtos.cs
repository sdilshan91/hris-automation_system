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

// ── Full-replace reviewer set (BUG-244 #1, AC-1/FR-2) ──────────────────────

/// <summary>One reviewer row in a full-replace save (BUG-244 #1). Only Peer / DirectReport are client-managed;
/// Self + Manager are server-owned and never sent. Matches the FE <c>IReviewerInput</c> (camelCase).</summary>
public sealed record ReviewerInputDto(Guid ReviewerId, ReviewerCategory Category);

/// <summary>
/// Full-replace request for an employee's manual (Peer + Direct Report) 360 reviewers (BUG-244 #1). The
/// reviewee rides the route; the active 360-enabled cycle is resolved server-side. Self + Manager are
/// server-owned and left untouched. Matches the FE <c>ISaveReviewersRequest</c>.
/// </summary>
public sealed record SaveReviewersRequest(IReadOnlyList<ReviewerInputDto> Reviewers);

// ── Feedback form for one assignment (BUG-244 #2, FR-4/AC-2) ────────────────

/// <summary>
/// One question card on a reviewer's 360 feedback form (BUG-244 #2). Projected from the reviewee's cycle goals
/// (<see cref="Kind"/> = "Goal", <see cref="QuestionId"/> = goal id); <see cref="Rating"/>/<see cref="Comment"/>
/// are hydrated from the persisted feedback when already submitted, else null/empty. Matches the FE
/// <c>IFeedbackQuestion</c>.
/// </summary>
public sealed record FeedbackFormQuestionDto
{
    public Guid QuestionId { get; init; }
    /// <summary>"Competency" | "Goal" — currently always "Goal" (questions come from the reviewee's goals).</summary>
    public string Kind { get; init; } = "Goal";
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    /// <summary>The reviewer's rating on the tenant scale; null while unrated/unsubmitted.</summary>
    public int? Rating { get; init; }
    public string Comment { get; init; } = string.Empty;
}

/// <summary>
/// One reviewer's 360 feedback form for a single assignment (BUG-244 #2 / FR-4 / AC-2 deep link). The reviewer
/// is resolved server-side and RLS ensures a reviewer only loads their OWN assignment. Matches the FE
/// <c>IFeedbackForm</c> (camelCase).
/// </summary>
public sealed record FeedbackFormDto
{
    public Guid AssignmentId { get; init; }
    public Guid CycleId { get; init; }
    public string CycleName { get; init; } = string.Empty;
    public Guid RevieweeId { get; init; }
    public string RevieweeName { get; init; } = string.Empty;
    public string? RevieweeJobTitle { get; init; }
    public ReviewerCategory Category { get; init; }
    public int RatingScaleMax { get; init; }
    /// <summary>Once true the form is read-only (BR-3 — the reviewer has submitted).</summary>
    public bool Submitted { get; init; }
    public DateTime? SubmittedOn { get; init; }
    public bool Anonymous { get; init; }
    public IReadOnlyList<FeedbackFormQuestionDto> Questions { get; init; } = [];
}

// ── Completion tracker (BUG-244 #3, AC-3) ──────────────────────────────────

/// <summary>Per-category progress for the completion tracker (BUG-244 #3). Matches the FE <c>ICategoryProgress</c>.</summary>
public sealed record CategoryProgressDto
{
    public ReviewerCategory Category { get; init; }
    public int Submitted { get; init; }
    public int Pending { get; init; }
    /// <summary>Always 0 — there is no 360 deadline/window entity yet (BUG-244 #3).</summary>
    public int Overdue { get; init; }
    /// <summary>Configured minimum for this category: <c>Min360PeerReviewers</c> for Peer, 0 for the rest.</summary>
    public int Minimum { get; init; }
}

/// <summary>The 360 completion tracker across all four categories for one reviewee (BUG-244 #3). Matches the FE
/// <c>ICompletionTracker</c>.</summary>
public sealed record CompletionTrackerDto
{
    public Guid EmployeeId { get; init; }
    public IReadOnlyList<CategoryProgressDto> Categories { get; init; } = [];
}

// ── Submit-by-assignment (BUG-244, reviewer form submit) ───────────────────

/// <summary>
/// One answer posted from the reviewer's feedback form (BUG-244 submit). <see cref="QuestionId"/> is the form
/// question id — for #2's form this is the GOAL id (kind="Goal"), so it maps to <c>Feedback360ItemInput.GoalId</c>
/// on the way back in (an exact round-trip with the #2 form projection). Matches the FE <c>IFeedbackAnswerInput</c>.
/// </summary>
public sealed record FeedbackAnswerInput
{
    public Guid QuestionId { get; init; }
    /// <summary>Rating on the tenant scale; may arrive null (unrated) — the service rejects an out-of-range rating.</summary>
    public int? Rating { get; init; }
    public string? Comment { get; init; }
}

/// <summary>
/// Submit-by-assignment request (BUG-244). The assignment rides the route; the reviewer + cycle + reviewee are
/// resolved from it server-side (RLS: caller's employee must be the assignment's reviewer). Matches the FE
/// <c>ISubmitFeedbackRequest</c>.
/// </summary>
public sealed record SubmitFeedbackByAssignmentRequest(IReadOnlyList<FeedbackAnswerInput> Answers);

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

    /// <summary>
    /// GAP-012 / ISSUE-373: the cycle's display name (<c>AppraisalCycle.Name</c>), read by the results header
    /// and previously never sent.
    /// </summary>
    public string CycleName { get; init; } = string.Empty;

    public Guid RevieweeEmployeeId { get; init; }
    public string RevieweeName { get; init; } = string.Empty;

    /// <summary>
    /// GAP-012 / ISSUE-373: the reviewee's job title, shown beside the name. Requires the loading query to
    /// Include the JobTitle nav, or this ships as an empty string.
    /// </summary>
    public string JobTitle { get; init; } = string.Empty;
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
