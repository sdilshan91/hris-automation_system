using HRM.Domain.Enums;

namespace HRM.Application.Features.Onboarding.DTOs;

/// <summary>How to handle an assignment when the employee already has an active checklist (US-ONB-002 AC-3).</summary>
public enum ChecklistAssignmentMode
{
    /// <summary>Supersede the existing active checklist with a new version (BR-2).</summary>
    Replace = 0,

    /// <summary>Add the template's tasks onto the existing active checklist (AC-3 "add additional tasks").</summary>
    Merge = 1,
}

/// <summary>
/// Full assigned-checklist representation returned from assign / get / modify (US-ONB-002 §7 output).
/// </summary>
public sealed record OnboardingChecklistInstanceDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid TemplateId { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public OnboardingChecklistStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public int Version { get; init; }
    public Guid? AssignedByUserId { get; init; }
    public IReadOnlyList<OnboardingTaskInstanceDto> Tasks { get; init; } = [];
    public int TaskCount { get; init; }
    public int NotificationsQueued { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>A single assigned task instance (US-ONB-002 AC-2).</summary>
public sealed record OnboardingTaskInstanceDto
{
    public Guid Id { get; init; }
    public Guid? SourceTemplateTaskId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public OnboardingResponsibleRole ResponsibleRole { get; init; }
    public string ResponsibleRoleName { get; init; } = string.Empty;
    public Guid? ResponsibleUserId { get; init; }
    public DateTime DueDate { get; init; }
    public OnboardingTaskStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public bool IsMandatory { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>An applicable template row for the assignment dropdown (US-ONB-002 AC-1/FR-1).</summary>
public sealed record ApplicableTemplateDto
{
    public Guid Id { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsUniversal { get; init; }
    public int TaskCount { get; init; }
    public int MandatoryTaskCount { get; init; }
}

// ── Request bodies ───────────────────────────────────────────────────

/// <summary>Request body for an ad-hoc task added during assignment or modification (FR-5).</summary>
public sealed record AdHocTaskRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public OnboardingResponsibleRole ResponsibleRole { get; init; } = OnboardingResponsibleRole.HR;
    public Guid? ResponsibleUserId { get; init; }
    public int DueOffsetDays { get; init; }
    public bool IsMandatory { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>Request body for assigning a checklist to a new hire (US-ONB-002 §7 input).</summary>
public sealed record AssignChecklistRequest
{
    public Guid EmployeeId { get; init; }
    public Guid TemplateId { get; init; }
    public DateTime? OverrideStartDate { get; init; }
    public ChecklistAssignmentMode Mode { get; init; } = ChecklistAssignmentMode.Replace;
    public IReadOnlyList<AdHocTaskRequest> AdditionalTasks { get; init; } = [];
    public string? IdempotencyKey { get; init; }
}

/// <summary>A single task modification op when editing an assigned checklist (US-ONB-002 AC-4/FR-5/FR-6).</summary>
public sealed record ModifyTaskRequest
{
    public Guid TaskInstanceId { get; init; }
    public DateTime? NewDueDate { get; init; }
    public bool Remove { get; init; }
}

/// <summary>Request body for modifying an assigned checklist (US-ONB-002 AC-4).</summary>
public sealed record ModifyChecklistRequest
{
    public IReadOnlyList<AdHocTaskRequest> AddTasks { get; init; } = [];
    public IReadOnlyList<ModifyTaskRequest> TaskChanges { get; init; } = [];
}
