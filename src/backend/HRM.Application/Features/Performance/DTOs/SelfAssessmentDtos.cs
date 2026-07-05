using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.DTOs;

/// <summary>
/// The employee's self-assessment for one cycle: the overall status/score plus a per-goal item for every
/// assigned goal (US-PRF-002 AC-1). Goals with no saved rating yet come back with null rating fields.
/// </summary>
public sealed record SelfAssessmentDto
{
    public Guid Id { get; init; }
    public Guid CycleId { get; init; }
    public Guid EmployeeId { get; init; }
    public SelfAssessmentStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public decimal? WeightedSelfScore { get; init; }
    public DateTime? SubmittedAt { get; init; }

    /// <summary>Cycle's configured rating scale max (FR-2/BR-4), e.g. 5 for a 1-5 scale.</summary>
    public int RatingScaleMax { get; init; }

    /// <summary>Cycle's configured self-rating weight percent (BR-4), e.g. 30 for a 30:70 self:manager ratio.</summary>
    public int SelfWeightPercent { get; init; }

    /// <summary>True while the self-assessment window is open (AC-4). The FE renders read-only when false.</summary>
    public bool IsSelfAssessmentOpen { get; init; }

    /// <summary>True once submitted/locked (AC-2/BR-3).</summary>
    public bool IsLocked { get; init; }

    public IReadOnlyList<SelfAssessmentItemDto> Items { get; init; } = [];
}

/// <summary>One per-goal self-rating row, carrying the goal context for display + the saved rating (AC-1).</summary>
public sealed record SelfAssessmentItemDto
{
    public Guid GoalId { get; init; }
    public string GoalTitle { get; init; } = string.Empty;
    public string? GoalDescription { get; init; }
    public int GoalWeight { get; init; }
    public string GoalTargetValue { get; init; } = string.Empty;
    public string GoalMeasurementUnit { get; init; } = string.Empty;
    public DateOnly GoalDueDate { get; init; }

    public int? SelfRating { get; init; }
    public int? AchievementPercentage { get; init; }
    public string? Comment { get; init; }

    public IReadOnlyList<SelfAssessmentAttachmentDto> Attachments { get; init; } = [];
}

/// <summary>Evidence-file metadata attached to a goal item (FR-5). Never carries the file bytes.</summary>
public sealed record SelfAssessmentAttachmentDto
{
    public Guid Id { get; init; }
    public Guid GoalId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }

    /// <summary>UTC timestamp the evidence file was uploaded (ISSUE-105).</summary>
    public DateTime UploadedAt { get; init; }
}

// ── Request bodies ─────────────────────────────────────────────────────

/// <summary>One goal's rating in a save-draft/submit request (US-PRF-002 FR-1/FR-3/FR-6).</summary>
public sealed record SelfAssessmentItemInput(
    Guid GoalId,
    int? SelfRating,
    int? AchievementPercentage,
    string? Comment);

/// <summary>
/// Save-as-draft / submit request body for the active cycle's self-assessment (US-PRF-002 AC-2/AC-3).
/// CycleId identifies the active cycle the employee is rating against; the employee is resolved from the
/// authenticated caller (never supplied by the client — NFR-2).
/// </summary>
public sealed record SaveSelfAssessmentRequest(
    Guid CycleId,
    IReadOnlyList<SelfAssessmentItemInput> Items);

/// <summary>Service-layer input decoupling the handler records from the service (mirrors GoalInput).</summary>
public sealed record SaveSelfAssessmentInput(
    Guid CycleId,
    IReadOnlyList<SelfAssessmentItemInput> Items);

/// <summary>
/// Reopen request body (US-PRF-004 BR-3/AC-2 — BUG-059). The optional reason is recorded on the
/// SelfAssessment.Reopened audit row; the target employee + cycle come from the route.
/// </summary>
public sealed record ReopenSelfAssessmentRequest(string? Reason);
