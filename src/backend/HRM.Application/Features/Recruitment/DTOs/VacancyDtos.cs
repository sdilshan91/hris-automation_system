using HRM.Application.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

/// <summary>
/// Full vacancy representation for detail and create/update responses (US-REC-001 §7).
/// </summary>
public sealed record VacancyDto
{
    public Guid Id { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public VacancyStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public Guid? JobTitleId { get; init; }
    public string? JobTitleName { get; init; }
    public Guid? LocationId { get; init; }
    public string? LocationName { get; init; }
    public Guid? HiringManagerId { get; init; }
    public string? HiringManagerName { get; init; }
    public EmploymentType EmploymentType { get; init; }
    public string EmploymentTypeName { get; init; } = string.Empty;
    public int Headcount { get; init; }
    public int FilledCount { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public string? SalaryCurrency { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Qualifications { get; init; }
    public DateOnly? ApplicationDeadline { get; init; }
    public string? Slug { get; init; }
    public bool PublishToPublicCareers { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Lightweight vacancy row for the internal paged list view (NFR-1).
/// </summary>
public sealed record VacancyListItemDto
{
    public Guid Id { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public VacancyStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public string? JobTitleName { get; init; }
    public string? HiringManagerName { get; init; }
    public EmploymentType EmploymentType { get; init; }
    public string EmploymentTypeName { get; init; } = string.Empty;
    public int Headcount { get; init; }
    public int FilledCount { get; init; }
    public DateOnly? ApplicationDeadline { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Public careers-page vacancy summary (anonymous, FR-4/FR-5/NFR-5). Deliberately omits internal
/// fields (hiring manager, filled count, created-by, internal ids beyond what the public detail needs).
/// </summary>
public sealed record PublicVacancyListItemDto
{
    public string Slug { get; init; } = string.Empty;
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? DepartmentName { get; init; }
    public string? LocationName { get; init; }
    public EmploymentType EmploymentType { get; init; }
    public string EmploymentTypeName { get; init; } = string.Empty;
    public DateOnly? ApplicationDeadline { get; init; }
    public DateTime? PublishedAt { get; init; }
}

/// <summary>
/// Public careers-page vacancy detail (anonymous, FR-4/FR-5/NFR-5). Includes the sanitized HTML body.
/// </summary>
public sealed record PublicVacancyDetailDto
{
    public string Slug { get; init; } = string.Empty;
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? DepartmentName { get; init; }
    public string? LocationName { get; init; }
    public EmploymentType EmploymentType { get; init; }
    public string EmploymentTypeName { get; init; } = string.Empty;
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public string? SalaryCurrency { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Qualifications { get; init; }
    public DateOnly? ApplicationDeadline { get; init; }
    public DateTime? PublishedAt { get; init; }
}

// ── Request bodies ───────────────────────────────────────────────────

/// <summary>Request body for creating a vacancy (saved as Draft, AC-1).</summary>
public sealed record CreateVacancyRequest
{
    public string Title { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? HiringManagerId { get; init; }
    public EmploymentType EmploymentType { get; init; } = EmploymentType.FullTime;
    public int Headcount { get; init; } = 1;
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public string? SalaryCurrency { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Qualifications { get; init; }
    public DateOnly? ApplicationDeadline { get; init; }
    public bool PublishToPublicCareers { get; init; } = true;
}

/// <summary>Request body for updating a vacancy (AC-3).</summary>
public sealed record UpdateVacancyRequest
{
    public string Title { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? HiringManagerId { get; init; }
    public EmploymentType EmploymentType { get; init; } = EmploymentType.FullTime;
    public int Headcount { get; init; } = 1;
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public string? SalaryCurrency { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Qualifications { get; init; }
    public DateOnly? ApplicationDeadline { get; init; }
    public bool PublishToPublicCareers { get; init; } = true;
}

/// <summary>Request body for changing a vacancy's status (FR-2 transition validation).</summary>
public sealed record ChangeVacancyStatusRequest
{
    public VacancyStatus Status { get; init; }
}

/// <summary>Request body for bulk status change (FR-6).</summary>
public sealed record BulkChangeVacancyStatusRequest
{
    public IReadOnlyList<Guid> VacancyIds { get; init; } = [];
    public VacancyStatus Status { get; init; }
}

/// <summary>Per-item outcome of a bulk status change (FR-6).</summary>
public sealed record BulkStatusChangeItemResult
{
    public Guid VacancyId { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>Aggregate result of a bulk status change (FR-6).</summary>
public sealed record BulkStatusChangeResult
{
    public int SucceededCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<BulkStatusChangeItemResult> Results { get; init; } = [];
}
