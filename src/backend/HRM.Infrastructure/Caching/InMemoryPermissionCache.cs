using System.Collections.Concurrent;
using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// In-memory implementation of IPermissionCache using ConcurrentDictionary.
/// Suitable for single-instance dev/testing; invalidation is immediate.
///
/// TODO (NFR-2): Replace with Redis-backed implementation for production.
///   - Key pattern: t:{tenantId}:user:{userId}:permissions
///   - Use StackExchange.Redis SET/GET with JSON serialization
///   - TTL: 15 min sliding expiration
///   - InvalidateTenantAsync: use SCAN + DEL on pattern t:{tenantId}:user:*:permissions
/// </summary>
public sealed class InMemoryPermissionCache : IPermissionCache
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _cache = new();

    public Task<IReadOnlyList<string>?> GetPermissionsAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(tenantId, userId);
        _cache.TryGetValue(key, out var permissions);
        return Task.FromResult(permissions);
    }

    public Task SetPermissionsAsync(
        Guid tenantId, Guid userId, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(tenantId, userId);
        _cache[key] = permissions;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(tenantId, userId);
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task InvalidateTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var prefix = $"t:{tenantId}:user:";
        var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    private static string BuildKey(Guid tenantId, Guid userId) =>
        $"t:{tenantId}:user:{userId}:permissions";
}
