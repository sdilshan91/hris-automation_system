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

    /// <summary>
    /// ISSUE-364: the manager's display name. Absent before, while the FE model invented it — so the
    /// department form and tree rendered a permanently blank manager line. `JobTitleDto` already
    /// denormalizes `GradeName` the same way, so this is the established pattern, not a new one.
    /// </summary>
    public string? ManagerName { get; init; }

    /// <summary>
    /// ISSUE-364: count of ACTIVE employees in this department. Absent before, so the list rendered
    /// "undefined employees" and the deactivate dialog's active-employee warning could never fire
    /// (`undefined > 0` is false). Both sibling DTOs (JobTitle, Location) already return a count, which
    /// is what made departments the inconsistent one. Batched in the list query — never per row.
    /// </summary>
    public int EmployeeCount { get; init; }

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
