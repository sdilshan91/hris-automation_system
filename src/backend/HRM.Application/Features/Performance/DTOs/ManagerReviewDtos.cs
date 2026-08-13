using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.DTOs;

/// <summary>
/// The manager's review workspace for one employee + cycle (US-PRF-003 AC-1): the overall status/scores +
/// summary/flag, plus a per-goal item carrying the employee's self-rating/comment ALONGSIDE the manager's
/// own (possibly empty) rating/comment for side-by-side review.
/// </summary>
public sealed record ManagerReviewDto
{
    public Guid Id { get; init; }
    public Guid CycleId { get; init; }

    /// <summary>
    /// GAP-012 / ISSUE-373: the cycle's display name (<c>AppraisalCycle.Name</c>). The reviewer's header reads
    /// it; the API never sent it, so it rendered blank.
    /// </summary>
    public string CycleName { get; init; } = string.Empty;

    /// <summary>
    /// GAP-012 / ISSUE-373: the reviewee's job title. Shown beside the name so a manager reviewing several
    /// reports can tell them apart. Requires the loading query to Include(e => e.JobTitle) — without it this
    /// would silently be empty, which is how it would have shipped as a "fix" that changed nothing.
    /// </summary>
    public string JobTitle { get; init; } = string.Empty;
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;

    public ManagerReviewStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;

    /// <summary>Weighted manager score (AC-2). Null until submitted.</summary>
    public decimal? WeightedManagerScore { get; init; }

    /// <summary>The employee's weighted self-score (from their submitted self-assessment), or null if not submitted.</summary>
    public decimal? WeightedSelfScore { get; init; }

    /// <summary>Final combined score (BR-4). Null until the manager review is submitted.</summary>
    public decimal? FinalScore { get; init; }

    /// <summary>Overall manager summary comment (FR-5).</summary>
    public string? SummaryComment { get; init; }

    /// <summary>Optional disposition flag — None / Recognition / Promotion / Pip (FR-6).</summary>
    public ReviewFlag Flag { get; init; }
    public string FlagName { get; init; } = string.Empty;

    public DateTime? SubmittedAt { get; init; }

    /// <summary>Cycle's configured rating scale max (FR-2), e.g. 5 for a 1-5 scale.</summary>
    public int RatingScaleMax { get; init; }

    /// <summary>Cycle's configured self-rating weight percent (BR-4), e.g. 30 for a 30:70 self:manager ratio.</summary>
    public int SelfWeightPercent { get; init; }

    /// <summary>Manager weight percent = 100 - SelfWeightPercent (BR-4).</summary>
    public int ManagerWeightPercent { get; init; }

    /// <summary>True while the manager-review window is open (BR-1). The FE renders read-only when false.</summary>
    public bool IsReviewWindowOpen { get; init; }

    /// <summary>True once submitted/locked (AC-5/BR-1).</summary>
    public bool IsLocked { get; init; }

    /// <summary>True if the employee has submitted their self-assessment (drives the precondition / display).</summary>
    public bool SelfAssessmentSubmitted { get; init; }

    public IReadOnlyList<ManagerReviewItemDto> Items { get; init; } = [];
}

/// <summary>
/// One per-goal review row: the goal context, the employee's self-rating/comment (left side, AC-1) and the
/// manager's rating/comment (right side, FR-2/FR-3).
/// </summary>
public sealed record ManagerReviewItemDto
{
    public Guid GoalId { get; init; }
    public string GoalTitle { get; init; } = string.Empty;
    public string? GoalDescription { get; init; }
    public int GoalWeight { get; init; }
    public string GoalTargetValue { get; init; } = string.Empty;
    public string GoalMeasurementUnit { get; init; } = string.Empty;
    public DateOnly GoalDueDate { get; init; }

    // ── Employee self-assessment (read-only reference, AC-1/FR-1) ──
    public int? SelfRating { get; init; }
    public int? SelfAchievementPercentage { get; init; }
    public string? SelfComment { get; init; }

    // ── Manager rating (the editable side, FR-2/FR-3) ──
    public int? ManagerRating { get; init; }
    public string? ManagerComment { get; init; }
}

/// <summary>One team member's review status on the team-reviews dashboard (US-PRF-003 AC-4).</summary>
public sealed record TeamReviewStatusDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public int GoalCount { get; init; }

    /// <summary>
    /// Aggregate review status (AC-4): "PendingSelfAssessment" (employee hasn't submitted),
    /// "SelfAssessmentSubmitted" (ready for manager), "ManagerReviewPending" (manager started a draft), or
    /// "Completed" (manager review submitted).
    /// </summary>
    public string Status { get; init; } = "PendingSelfAssessment";

    public decimal? WeightedSelfScore { get; init; }
    public decimal? WeightedManagerScore { get; init; }
    public decimal? FinalScore { get; init; }
}

/// <summary>The team-reviews dashboard for a manager + cycle (US-PRF-003 AC-4).</summary>
public sealed record TeamReviewsDashboardDto
{
    public Guid CycleId { get; init; }
    public IReadOnlyList<TeamReviewStatusDto> Members { get; init; } = [];
}

// ── Request bodies ─────────────────────────────────────────────────────

/// <summary>One goal's manager rating in a save-draft/submit request (US-PRF-003 FR-2/FR-3).</summary>
public sealed record ManagerReviewItemInput(
    Guid GoalId,
    int? ManagerRating,
    string? ManagerComment);

/// <summary>
/// Save-as-draft / submit request body for a manager review of an employee for a cycle (US-PRF-003).
/// The reviewing manager is resolved from the authenticated caller (never supplied — NFR-2).
/// </summary>
public sealed record SaveManagerReviewRequest(
    Guid CycleId,
    Guid EmployeeId,
    string? SummaryComment,
    ReviewFlag Flag,
    IReadOnlyList<ManagerReviewItemInput> Items);

/// <summary>Service-layer input decoupling the handler records from the service.</summary>
public sealed record SaveManagerReviewInput(
    Guid CycleId,
    Guid EmployeeId,
    string? SummaryComment,
    ReviewFlag Flag,
    IReadOnlyList<ManagerReviewItemInput> Items);
