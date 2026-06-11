namespace HRM.Application.Features.Departments.DTOs;

/// <summary>
/// Flat DTO for a single department (list/detail views).
/// </summary>
public sealed record DepartmentDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public string? ParentDepartmentName { get; init; }
    public Guid? ManagerId { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Tree-shaped DTO for hierarchy rendering (FR-8).
/// </summary>
public sealed record DepartmentTreeNodeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ManagerId { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<DepartmentTreeNodeDto> Children { get; init; } = [];
}

/// <summary>
/// Request body for creating a department.
/// </summary>
public sealed record CreateDepartmentRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public Guid? ManagerId { get; init; }
}

/// <summary>
/// Request body for updating a department.
/// </summary>
public sealed record UpdateDepartmentRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public Guid? ManagerId { get; init; }
}
