using HRM.Domain.Enums;

namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// DTO for directory listing — shape varies by caller role (US-CHR-003 FR-9, BR-2/3/4).
/// All roles see the "basic" fields; HR Officers see the full set.
/// Sensitive fields (email, phone, dateOfJoining) are null when stripped for Employee role.
/// </summary>
public sealed record EmployeeDirectoryItemDto
{
    public Guid Id { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? DepartmentName { get; init; }
    public string? JobTitleName { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? EmploymentType { get; init; }
    public DateTime? DateOfJoining { get; init; }
    public string? ProfilePhotoUrl { get; init; }
    public string? Location { get; init; }
}

/// <summary>
/// Paginated result for the employee directory (US-CHR-003 AC-4).
/// Response shape: { data, total, page, pageSize }.
/// </summary>
public sealed record EmployeeDirectoryResult
{
    public IReadOnlyList<EmployeeDirectoryItemDto> Data { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Enumerates the visibility level for directory field stripping (US-CHR-003 BR-2/3/4).
/// </summary>
public enum DirectoryFieldVisibility
{
    /// <summary>HR Officer / Tenant Admin — sees all fields.</summary>
    Full,

    /// <summary>Manager — sees reporting chain employees with email/phone/dateOfJoining.</summary>
    Manager,

    /// <summary>Employee — basic directory: name, photo, department, job title, status, location.</summary>
    Basic,
}
