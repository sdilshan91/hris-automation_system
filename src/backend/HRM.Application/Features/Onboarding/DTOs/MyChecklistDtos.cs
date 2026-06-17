using HRM.Domain.Enums;

namespace HRM.Application.Features.Onboarding.DTOs;

/// <summary>
/// US-ONB-003 AC-1/AC-2/FR-1: the logged-in employee's assigned onboarding checklist with tasks grouped by
/// category and a progress summary. Matches the pinned frontend contract for
/// GET /api/v1/onboarding/checklists/me.
/// </summary>
public sealed record MyChecklistDto
{
    public Guid ChecklistInstanceId { get; init; }
    public OnboardingChecklistStatus Status { get; init; }
    public int ProgressPercent { get; init; }
    public int TotalTasks { get; init; }
    public int CompletedTasks { get; init; }
    public int PendingTasks { get; init; }
    public int OverdueTasks { get; init; }
    public IReadOnlyList<MyChecklistCategoryDto> Categories { get; init; } = [];
}

/// <summary>A collapsible category group of the employee's tasks (US-ONB-003 AC-2).</summary>
public sealed record MyChecklistCategoryDto
{
    public string Category { get; init; } = string.Empty;
    public IReadOnlyList<MyChecklistTaskDto> Tasks { get; init; } = [];
}

/// <summary>A single task row in the employee's checklist view (US-ONB-003 AC-2/AC-3/AC-4).</summary>
public sealed record MyChecklistTaskDto
{
    public Guid TaskInstanceId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public OnboardingResponsibleRole ResponsibleRole { get; init; }
    public DateTime DueDate { get; init; }
    /// <summary>Effective status — pending/in-progress/completed/overdue (overdue computed at read time, AC-5).</summary>
    public string Status { get; init; } = string.Empty;
    public bool IsMandatory { get; init; }
    public bool RequiresDocument { get; init; }
    public DateTime? CompletedAt { get; init; }
    public Guid? CompletedBy { get; init; }
    public string? Comment { get; init; }
    public string? AttachmentUrl { get; init; }
}

/// <summary>
/// US-ONB-003 AC-1/FR-4: the cheap progress widget summary for the dashboard
/// (GET /api/v1/onboarding/checklists/me/progress).
/// </summary>
public sealed record MyChecklistProgressDto
{
    public int ProgressPercent { get; init; }
    public int TotalTasks { get; init; }
    public int CompletedTasks { get; init; }
    public int PendingTasks { get; init; }
    public int OverdueTasks { get; init; }
}

/// <summary>
/// US-ONB-003 AC-3/AC-4: the response of completing a task — the updated task plus the recalculated
/// progress so the UI can update the ring in real time without a second round-trip.
/// </summary>
public sealed record CompleteTaskResultDto
{
    public MyChecklistTaskDto Task { get; init; } = new();
    public MyChecklistProgressDto Progress { get; init; } = new();
}
