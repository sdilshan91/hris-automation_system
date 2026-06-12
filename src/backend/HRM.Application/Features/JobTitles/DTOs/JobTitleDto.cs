using HRM.Domain.Enums;

namespace HRM.Application.Features.JobTitles.DTOs;

/// <summary>
/// Flat DTO for a single job title (list/detail views).
/// </summary>
public sealed record JobTitleDto
{
    public Guid Id { get; init; }
    public string TitleName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? GradeId { get; init; }
    /// <summary>
    /// Count of active employees assigned to this job title.
    /// TODO(US-CHR-001): Wire to real employee count when Employee entity exists.
    /// Currently always returns 0.
    /// </summary>
    public int EmployeeCount { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Request body for creating a job title.
/// </summary>
public sealed record CreateJobTitleRequest
{
    public string TitleName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? GradeId { get; init; }
}

/// <summary>
/// Request body for updating a job title.
/// </summary>
public sealed record UpdateJobTitleRequest
{
    public string TitleName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? GradeId { get; init; }
}

/// <summary>
/// DTO representing an employment type reference value (FR-6).
/// </summary>
public sealed record EmploymentTypeDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
