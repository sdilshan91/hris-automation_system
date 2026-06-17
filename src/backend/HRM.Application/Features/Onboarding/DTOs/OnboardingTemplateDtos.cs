using HRM.Domain.Enums;

namespace HRM.Application.Features.Onboarding.DTOs;

/// <summary>
/// Full onboarding checklist template representation for detail and create/clone responses (US-ONB-001 §7).
/// </summary>
public sealed record OnboardingTemplateDto
{
    public Guid Id { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<Guid> ApplicableDepartments { get; init; } = [];
    public IReadOnlyList<Guid> ApplicableJobTitles { get; init; } = [];
    public bool IsActive { get; init; }
    public bool IsUniversal { get; init; }
    public IReadOnlyList<OnboardingTemplateTaskDto> Tasks { get; init; } = [];
    public int TaskCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>A single task within a template (US-ONB-001 FR-3).</summary>
public sealed record OnboardingTemplateTaskDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public OnboardingResponsibleRole ResponsibleRole { get; init; }
    public string ResponsibleRoleName { get; init; } = string.Empty;
    public Guid? ResponsibleUserId { get; init; }
    public int DueOffsetDays { get; init; }
    public bool IsMandatory { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>Lightweight template row for the paged list view (NFR-1).</summary>
public sealed record OnboardingTemplateListItemDto
{
    public Guid Id { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public bool IsUniversal { get; init; }
    public int TaskCount { get; init; }
    public int MandatoryTaskCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Generic paged result envelope for list endpoints.</summary>
public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

// ── Request bodies ───────────────────────────────────────────────────

/// <summary>Request body for a single task when creating/cloning a template (FR-3).</summary>
public sealed record OnboardingTemplateTaskRequest
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

/// <summary>Request body for creating a checklist template (AC-1/AC-2).</summary>
public sealed record CreateOnboardingTemplateRequest
{
    public string TemplateName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<Guid> ApplicableDepartments { get; init; } = [];
    public IReadOnlyList<Guid> ApplicableJobTitles { get; init; } = [];
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<OnboardingTemplateTaskRequest> Tasks { get; init; } = [];
}

/// <summary>Request body for cloning a template (FR-6). An optional new name overrides the "(Copy)" default.</summary>
public sealed record CloneOnboardingTemplateRequest
{
    public string? NewName { get; init; }
}
