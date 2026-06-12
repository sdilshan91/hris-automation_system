using HRM.Domain.Enums;

namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// Full DTO for a single employee (detail views).
/// </summary>
public sealed record EmployeeDto
{
    public Guid Id { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public DateTime DateOfJoining { get; init; }
    public Guid DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public Guid JobTitleId { get; init; }
    public string? JobTitleName { get; init; }
    public string EmploymentType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ProfilePhotoUrl { get; init; }
    public string? Location { get; init; }
    public string? CustomFields { get; init; }
    public Guid? UserId { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Paginated list result for employee queries.
/// </summary>
public sealed record EmployeeListResult
{
    public IReadOnlyList<EmployeeDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Request body for creating an employee (US-CHR-001 AC-2).
/// </summary>
public sealed record CreateEmployeeRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public Gender? Gender { get; init; }
    public DateTime DateOfJoining { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid JobTitleId { get; init; }
    public EmploymentType EmploymentType { get; init; }
    public EmployeeStatus? Status { get; init; }
    public string? Location { get; init; }
    public string? CustomFields { get; init; }
    public Guid? UserId { get; init; }
}
