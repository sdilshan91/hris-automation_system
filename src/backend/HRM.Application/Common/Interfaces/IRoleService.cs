using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for RBAC operations: role CRUD, user-role assignment,
/// and permission resolution. Implemented in Infrastructure layer with EF Core.
/// </summary>
public interface IRoleService
{
    /// <summary>
    /// Lists all roles (built-in + custom) for the current tenant, including
    /// their permission lists and user counts.
    /// </summary>
    Task<Result<IReadOnlyList<RoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single role by ID within the current tenant.
    /// </summary>
    Task<Result<RoleDto>> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new custom role in the current tenant.
    /// System role names cannot be used in regular tenants.
    /// </summary>
    Task<Result<RoleDto>> CreateRoleAsync(string name, string? description, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing custom role. Built-in roles cannot be updated.
    /// </summary>
    Task<Result<RoleDto>> UpdateRoleAsync(Guid roleId, string name, string? description, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a custom role. Built-in roles cannot be deleted.
    /// Assigned users have the role removed from their assignments.
    /// </summary>
    Task<Result> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a set of roles to a user's tenant membership.
    /// Replaces existing assignments. Protects sole Tenant Owner.
    /// </summary>
    Task<Result> AssignUserRolesAsync(Guid userTenantId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the effective permissions for a user in a tenant.
    /// Returns the union of all permissions from assigned roles.
    /// Uses IPermissionCache when available.
    /// </summary>
    Task<IReadOnlyList<string>> ResolvePermissionsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
