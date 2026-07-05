using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// RBAC service: role CRUD, user-role assignment, and permission resolution.
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class RoleService : IRoleService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionCache _permissionCache;
    private readonly ILogger<RoleService> _logger;
    // ISSUE-056: the distributed cache backing AuthService.GetMyTenantsAsync, used to invalidate a user's
    // cached my-tenants entry when their roles change. Optional (nullable, default null) so isolated unit
    // construction that omits it still compiles (mirrors AuthService's optional ICurrentUser); the DI
    // container injects the registered IDistributedCache in production.
    private readonly IDistributedCache? _cache;

    public RoleService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPermissionCache permissionCache,
        ILogger<RoleService> logger,
        IDistributedCache? cache = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _permissionCache = permissionCache;
        _logger = logger;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<RoleDto>>.Failure("Tenant context is not resolved.", 400);

        var roles = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId)
            .Include(r => r.RolePermissions)
            .Include(r => r.UserTenantRoles)
            .OrderBy(r => r.IsBuiltIn ? 0 : 1)
            .ThenBy(r => r.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dtos = roles.Select(ToDto).ToList();
        return Result<IReadOnlyList<RoleDto>>.Success(dtos);
    }

    public async Task<Result<RoleDto>> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RoleDto>.Failure("Tenant context is not resolved.", 400);

        var role = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && r.Id == roleId)
            .Include(r => r.RolePermissions)
            .Include(r => r.UserTenantRoles)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null)
            return Result<RoleDto>.Failure("Role not found.", 404);

        return Result<RoleDto>.Success(ToDto(role));
    }

    public async Task<Result<RoleDto>> CreateRoleAsync(
        string name,
        string? description,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RoleDto>.Failure("Tenant context is not resolved.", 400);

        // FR-9: System roles cannot be created in regular tenants
        if (PermissionCatalog.SystemRoles.All.Contains(name, StringComparer.OrdinalIgnoreCase))
            return Result<RoleDto>.Failure("System roles cannot be created in regular tenants.", 400);

        // Check duplicate name within tenant
        var exists = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && r.Name == name)
            .AnyAsync(cancellationToken);

        if (exists)
            return Result<RoleDto>.Failure($"A role named '{name}' already exists in this tenant.", 400);

        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = name,
            Description = description,
            IsBuiltIn = false,
            CreatedAt = DateTime.UtcNow,
        };

        foreach (var perm in permissions.Distinct())
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = perm });
        }

        _dbContext.Roles.Add(role);
        // BUG-041 (FR-7): write a queryable RBAC audit trail, not just an ILogger line.
        AddRbacAudit("Role.Created", "Role", role.Id.ToString(),
            $"Role '{role.Name}' created with {permissions.Count} permission(s).");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "RBAC Audit: Role created. RoleId={RoleId}, Name={RoleName}, TenantId={TenantId}, By={UserId}, PermissionCount={PermCount}",
            role.Id, role.Name, _tenantContext.TenantId, _currentUser.Email, permissions.Count);

        return Result<RoleDto>.Success(ToDto(role));
    }

    public async Task<Result<RoleDto>> UpdateRoleAsync(
        Guid roleId,
        string name,
        string? description,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RoleDto>.Failure("Tenant context is not resolved.", 400);

        var role = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && r.Id == roleId)
            .Include(r => r.RolePermissions)
            .Include(r => r.UserTenantRoles)
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null)
            return Result<RoleDto>.Failure("Role not found.", 404);

        // FR-2: Built-in roles cannot be edited
        if (role.IsBuiltIn)
            return Result<RoleDto>.Failure("Built-in roles cannot be modified.", 400);

        // Check duplicate name (excluding self)
        var duplicate = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && r.Name == name && r.Id != roleId)
            .AnyAsync(cancellationToken);

        if (duplicate)
            return Result<RoleDto>.Failure($"A role named '{name}' already exists in this tenant.", 400);

        role.Name = name;
        role.Description = description;

        // Replace permissions
        _dbContext.RolePermissions.RemoveRange(role.RolePermissions);
        role.RolePermissions.Clear();

        foreach (var perm in permissions.Distinct())
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = perm });
        }

        // BUG-041 (FR-7): queryable RBAC audit trail.
        AddRbacAudit("Role.Updated", "Role", role.Id.ToString(),
            $"Role '{role.Name}' updated ({permissions.Distinct().Count()} permission(s)).");
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate cache for all users who have this role
        await InvalidateCacheForRoleUsersAsync(role, cancellationToken);

        _logger.LogInformation(
            "RBAC Audit: Role updated. RoleId={RoleId}, Name={RoleName}, TenantId={TenantId}, By={UserId}",
            role.Id, role.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result<RoleDto>.Success(ToDto(role));
    }

    public async Task<Result> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var role = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && r.Id == roleId)
            .Include(r => r.UserTenantRoles)
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null)
            return Result.Failure("Role not found.", 404);

        // FR-2 / AC-6: Built-in roles cannot be deleted
        if (role.IsBuiltIn)
            return Result.Failure("Built-in roles cannot be deleted.", 400);

        // Invalidate cache for affected users before deletion
        await InvalidateCacheForRoleUsersAsync(role, cancellationToken);

        // BR-7: Removing role from assigned users (cascade delete handles user_tenant_role rows)
        _dbContext.Roles.Remove(role);
        // BUG-041 (FR-7): queryable RBAC audit trail.
        AddRbacAudit("Role.Deleted", "Role", role.Id.ToString(),
            $"Role '{role.Name}' deleted (affected {role.UserTenantRoles.Count} user(s)).");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "RBAC Audit: Role deleted. RoleId={RoleId}, Name={RoleName}, TenantId={TenantId}, By={UserId}, AffectedUsers={UserCount}",
            roleId, role.Name, _tenantContext.TenantId, _currentUser.Email, role.UserTenantRoles.Count);

        return Result.Success();
    }

    public async Task<Result> AssignUserRolesAsync(
        Guid userTenantId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        // Load the user-tenant membership
        var userTenant = await _dbContext.UserTenants
            .Where(ut => ut.TenantId == _tenantContext.TenantId && ut.Id == userTenantId)
            .Include(ut => ut.UserTenantRoles)
                .ThenInclude(utr => utr.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (userTenant is null)
            return Result.Failure("User membership not found in this tenant.", 404);

        // Validate all roleIds belong to this tenant
        var validRoles = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && roleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (validRoles.Count != roleIds.Distinct().Count())
            return Result.Failure("One or more role IDs are invalid for this tenant.", 400);

        // FR-9: System roles cannot be assigned in regular tenants
        var hasSystemRole = validRoles.Any(r => PermissionCatalog.SystemRoles.All.Contains(r.Name));
        if (hasSystemRole && !_tenantContext.IsSystemContext)
            return Result.Failure("System roles cannot be assigned in regular tenants.", 400);

        // FR-8 / BR-6: Tenant Owner protection
        var tenantOwnerRole = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && r.Name == PermissionCatalog.BuiltInRoles.TenantOwner)
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantOwnerRole is not null)
        {
            var currentlyHasTenantOwner = userTenant.UserTenantRoles.Any(utr => utr.RoleId == tenantOwnerRole.Id);
            var newSetHasTenantOwner = roleIds.Contains(tenantOwnerRole.Id);

            if (currentlyHasTenantOwner && !newSetHasTenantOwner)
            {
                // Removing Tenant Owner from this user - check if they are the sole owner
                var otherOwnerCount = await _dbContext.UserTenantRoles
                    .IgnoreQueryFilters()
                    .CountAsync(
                        utr => utr.RoleId == tenantOwnerRole.Id
                            && utr.UserTenantId != userTenantId
                            && utr.UserTenant.TenantId == _tenantContext.TenantId,
                        cancellationToken);

                if (otherOwnerCount == 0)
                    return Result.Failure("Cannot remove the Tenant Owner role from the sole owner. Assign another owner first.", 400);
            }
        }

        // Replace assignments
        _dbContext.UserTenantRoles.RemoveRange(userTenant.UserTenantRoles);

        foreach (var roleId in roleIds.Distinct())
        {
            _dbContext.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = userTenantId,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = _currentUser.Email,
            });
        }

        // BUG-041 (FR-7): queryable RBAC audit trail for privilege changes.
        AddRbacAudit("UserRoles.Updated", "UserTenant", userTenantId.ToString(),
            $"Roles for membership {userTenantId} set to [{string.Join(",", roleIds)}].");
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate permission cache for this user
        await _permissionCache.InvalidateAsync(_tenantContext.TenantId, userTenant.UserId, cancellationToken);

        // ISSUE-056: a role change alters the roles list returned by AuthService.GetMyTenantsAsync, so drop
        // its cached my-tenants entry for this user (else it stays stale up to the 5-min TTL).
        await InvalidateMyTenantsCacheAsync(userTenant.UserId, cancellationToken);

        _logger.LogInformation(
            "RBAC Audit: User roles updated. UserTenantId={UserTenantId}, TenantId={TenantId}, By={UserId}, NewRoleIds={RoleIds}",
            userTenantId, _tenantContext.TenantId, _currentUser.Email, string.Join(",", roleIds));

        return Result.Success();
    }

    /// <summary>
    /// BUG-041 (FR-7): appends a queryable RBAC audit row to the shared audit_log table. Tenant/actor are
    /// stamped from context. Added to the change tracker; persisted by the caller's SaveChanges.
    /// </summary>
    private void AddRbacAudit(string eventType, string resourceType, string resourceId, string detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = eventType,
            Action = eventType,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// ISSUE-056: drop the cached my-tenants entry (<c>user:{userId}:tenants</c>) for a user whose tenant
    /// membership just changed, using the same key + IDistributedCache abstraction as
    /// <c>AuthService.GetMyTenantsAsync</c>. Best-effort (fail-soft): a cache outage must not fail the
    /// membership mutation — the entry then expires on its own TTL (mirrors the BUG-121 cache handling).
    /// </summary>
    private async Task InvalidateMyTenantsCacheAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_cache is null)
            return;

        try
        {
            await _cache.RemoveAsync($"user:{userId}:tenants", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "my-tenants cache invalidation failed for user {UserId}; entry will expire on its TTL", userId);
        }
    }

    public async Task<IReadOnlyList<string>> ResolvePermissionsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cached = await _permissionCache.GetPermissionsAsync(tenantId, userId, cancellationToken);
        if (cached is not null)
            return cached;

        // Cache miss: resolve from database
        var permissions = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .Where(ut => ut.UserId == userId && ut.TenantId == tenantId && ut.Status == UserTenantStatus.Active)
            .SelectMany(ut => ut.UserTenantRoles)
            .SelectMany(utr => utr.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);

        await _permissionCache.SetPermissionsAsync(tenantId, userId, permissions, cancellationToken);

        return permissions;
    }

    private async Task InvalidateCacheForRoleUsersAsync(Role role, CancellationToken cancellationToken)
    {
        // Get all user IDs that have this role in the current tenant
        var userIds = await _dbContext.UserTenantRoles
            .Where(utr => utr.RoleId == role.Id)
            .Select(utr => utr.UserTenant.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            await _permissionCache.InvalidateAsync(_tenantContext.TenantId, userId, cancellationToken);
        }
    }

    private static RoleDto ToDto(Role role) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Description = role.Description,
        IsBuiltIn = role.IsBuiltIn,
        Permissions = role.RolePermissions.Select(rp => rp.Permission).OrderBy(p => p).ToList(),
        UserCount = role.UserTenantRoles.Count,
        CreatedAt = role.CreatedAt,
    };
}
