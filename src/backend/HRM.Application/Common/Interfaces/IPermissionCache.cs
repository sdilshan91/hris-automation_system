namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Abstraction for caching a user's effective permissions within a tenant.
/// The default implementation is in-memory; production should use Redis
/// with key pattern: t:{tenantId}:user:{userId}:permissions
///
/// TODO (NFR-2): Swap to Redis-backed implementation once Redis is wired in.
///   - Key: t:{tenantId}:user:{userId}:permissions
///   - Invalidation: call InvalidateAsync on any role/permission change for that user+tenant.
///   - TTL: 15 min sliding expiration recommended.
/// </summary>
public interface IPermissionCache
{
    /// <summary>
    /// Gets the user's effective permissions for a tenant.
    /// Returns null on cache miss.
    /// </summary>
    Task<IReadOnlyList<string>?> GetPermissionsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the user's effective permissions for a tenant.
    /// </summary>
    Task SetPermissionsAsync(Guid tenantId, Guid userId, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached permissions for a user in a tenant.
    /// Must be called when roles/permissions change.
    /// </summary>
    Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached permissions for every user in a tenant.
    /// Called when a role's permissions are modified.
    /// </summary>
    Task InvalidateTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
