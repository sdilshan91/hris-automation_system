namespace HRM.Application.Features.Roles.DTOs;

/// <summary>
/// Role list/detail DTO returned by role endpoints.
/// </summary>
public sealed record RoleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsBuiltIn { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public int UserCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Request body for creating a custom role.
/// </summary>
public sealed record CreateRoleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

/// <summary>
/// Request body for updating a custom role.
/// </summary>
public sealed record UpdateRoleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

/// <summary>
/// Request body for assigning roles to a user's tenant membership.
/// </summary>
public sealed record AssignUserRolesRequest
{
    public IReadOnlyList<Guid> RoleIds { get; init; } = [];
}

/// <summary>
/// Response DTO for the permission catalog.
/// </summary>
public sealed record PermissionCatalogDto
{
    public IReadOnlyList<string> AllPermissions { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ByModule { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}
