using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.DTOs;

// ── Output DTOs ─────────────────────────────────────────────────────────

/// <summary>One evidence-attachment metadata row on a progress update (US-PRF-009 FR-2).</summary>
public sealed record GoalProgressAttachmentDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public long SizeBytes { get; init; }
}

/// <summary>One immutable progress update on a goal — a timeline entry (US-PRF-009 AC-3/FR-3/NFR-3).</summary>
public sealed record GoalProgressUpdateDto
{
    public Guid Id { get; init; }
    public Guid GoalId { get; init; }
    public Guid EmployeeId { get; init; }
    public int ProgressPct { get; init; }
    public GoalProgressStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<GoalProgressAttachmentDto> Attachments { get; init; } = [];
}

/// <summary>One manager/HR comment in a goal's thread (US-PRF-009 FR-8).</summary>
public sealed record GoalCommentDto
{
    public Guid Id { get; init; }
    public Guid GoalId { get; init; }
    public Guid? ProgressUpdateId { get; init; }
    public Guid AuthorEmployeeId { get; init; }
    public string AuthorName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// One of the employee's goals with its CURRENT progress derived from the latest update (US-PRF-009 AC-1). Drives
/// the "My Goals" cards: title/target, current progress %, status, last-update date and the progress-bar data.
/// </summary>
public sealed record MyGoalProgressDto
{
    public Guid GoalId { get; init; }
    public Guid CycleId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public GoalCategory Category { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public int Weight { get; init; }
    public string TargetValue { get; init; } = string.Empty;
    public string MeasurementUnit { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }

    /// <summary>Current progress % = the latest update's ProgressPct, else 0 if none posted yet (AC-1).</summary>
    public int CurrentProgressPct { get; init; }

    /// <summary>Current status = the latest update's status, else NotStarted (AC-1).</summary>
    public GoalProgressStatus CurrentStatus { get; init; }
    public string CurrentStatusName { get; init; } = string.Empty;

    /// <summary>The timestamp of the latest update, or null if no update has been posted yet (AC-1).</summary>
    public DateTime? LastUpdatedAt { get; init; }

    /// <summary>The count of progress updates posted against this goal (AC-1/AC-3).</summary>
    public int UpdateCount { get; init; }

    /// <summary>True when the daily stale-goal sweep would flag this goal "Needs Attention" (AC-5/FR-6/BR-4).</summary>
    public bool NeedsAttention { get; init; }
}

/// <summary>The full progress history for one goal — a chronological timeline (US-PRF-009 AC-3/FR-3).</summary>
public sealed record GoalTimelineDto
{
    public Guid GoalId { get; init; }
    public string Title { get; init; } = string.Empty;
    public int CurrentProgressPct { get; init; }
    public GoalProgressStatus CurrentStatus { get; init; }
    public string CurrentStatusName { get; init; } = string.Empty;
    public IReadOnlyList<GoalProgressUpdateDto> Updates { get; init; } = [];
    public IReadOnlyList<GoalCommentDto> Comments { get; init; } = [];
}

/// <summary>One direct report's goal-progress summary row for the manager's Team Goals view (US-PRF-009 AC-4).</summary>
public sealed record TeamGoalProgressRowDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public int GoalCount { get; init; }

    /// <summary>Weighted overall goal completion % across the employee's active goals (FR-4).</summary>
    public int OverallCompletionPct { get; init; }

    /// <summary>Count of the employee's goals currently AtRisk or Blocked (AC-4).</summary>
    public int AtRiskGoalCount { get; init; }

    /// <summary>Count of the employee's goals flagged "Needs Attention" by the stale sweep (AC-4/AC-5).</summary>
    public int NeedsAttentionGoalCount { get; init; }

    /// <summary>The most recent update timestamp across all the employee's goals, or null (AC-4).</summary>
    public DateTime? LastUpdatedAt { get; init; }
}

// ── Request bodies (controller) ─────────────────────────────────────────

/// <summary>One evidence attachment's metadata in an add-update request (US-PRF-009 FR-2). The blob is pre-uploaded.</summary>
public sealed record GoalProgressAttachmentInput(
    string FileName,
    string StorageKey,
    string? ContentType,
    long SizeBytes);

/// <summary>
/// Add-progress-update request (US-PRF-009 AC-2/FR-1/FR-2). The employee + tenant are resolved server-side; only
/// the goal id, progress %, status, notes and optional pre-uploaded attachment metadata are sent. Append-only.
/// </summary>
public sealed record AddGoalProgressRequest(
    int ProgressPct,
    GoalProgressStatus Status,
    string? Notes,
    IReadOnlyList<GoalProgressAttachmentInput>? Attachments);

/// <summary>Add-comment request (US-PRF-009 FR-8). Manager or HR. Optionally anchored to a specific update.</summary>
public sealed record AddGoalCommentRequest(
    Guid? ProgressUpdateId,
    string Body);

// ── Service-layer inputs (decouple handlers from the service) ───────────

public sealed record AddGoalProgressInput(
    Guid GoalId,
    int ProgressPct,
    GoalProgressStatus Status,
    string? Notes,
    IReadOnlyList<GoalProgressAttachmentInput> Attachments);

public sealed record AddGoalCommentInput(
    Guid GoalId,
    Guid? ProgressUpdateId,
    string Body);
