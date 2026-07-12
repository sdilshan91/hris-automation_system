using System.Text.Json;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// Redis-backed <see cref="IPermissionCache"/> (P3-3, NFR-2). Replaces the per-instance
/// <see cref="InMemoryPermissionCache"/> so permission lookups scale across instances AND invalidations propagate
/// cross-instance (an in-memory cache is per-pod, so a role change on pod A left pod B serving stale perms).
/// Registered only when Redis is configured (see <c>DependencyInjection.AddInfrastructure</c>); otherwise the
/// in-memory cache is used. Routes through the SHARED <see cref="IConnectionMultiplexer"/> registered by the API
/// host's <c>AddSharedRedisMultiplexer</c> (same instance P3-2's <see cref="Services.RedisTokenDenylist"/> uses).
///
/// <para><b>Key scheme — version-prefixed so a tenant-wide invalidate needs NO SCAN:</b></para>
/// <list type="bullet">
///   <item>Per-user key: <c>hrm:perm:t:{tenantId}:v{ver}:u:{userId}</c> = JSON <c>string[]</c> of permissions,
///     15-min TTL.</item>
///   <item>Version key: <c>hrm:perm:t:{tenantId}:ver</c> — an INCR counter (absent ⇒ version 0). Bumping it on a
///     tenant-wide invalidate orphans every old-version user key (they expire via their own 15-min TTL), so
///     invalidation is O(1) with no SCAN/keyspace walk.</item>
/// </list>
///
/// <para><b>Fail-open by design.</b> The permission cache is an OPTIMISATION: the caller
/// (<c>RoleService.ResolvePermissionsAsync</c>) resolves from the database on a miss. So a null/unavailable
/// multiplexer, a Redis exception, a version-read failure, or a JSON parse error must degrade to a "cache miss"
/// (Get ⇒ <c>null</c>) or a "no-op" (Set/Invalidate) — NEVER an exception out of the cache, NEVER a denial. A Redis
/// blip must degrade to DB resolution, never lock users out or serve stale perms on a version-read failure.</para>
/// </summary>
public sealed class RedisPermissionCache : IPermissionCache
{
    // Permissions expire after 15 minutes (NFR-2). A fixed expiry (not sliding) is sufficient: mutation paths
    // invalidate explicitly, and the TTL is only the backstop for a missed invalidation.
    private static readonly TimeSpan PermissionsTtl = TimeSpan.FromMinutes(15);

    // The version counter self-cleans if a tenant goes idle. 30 days comfortably outlives any 15-min user key.
    private static readonly TimeSpan VersionTtl = TimeSpan.FromDays(30);

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisPermissionCache> _logger;

    public RedisPermissionCache(IConnectionMultiplexer multiplexer, ILogger<RedisPermissionCache> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    private static string VersionKey(Guid tenantId) => $"hrm:perm:t:{tenantId}:ver";

    private static string UserKey(Guid tenantId, long version, Guid userId) =>
        $"hrm:perm:t:{tenantId}:v{version}:u:{userId}";

    /// <summary>Reads the current tenant permission-version (absent/unparseable ⇒ 0).</summary>
    private static async Task<long> ReadVersionAsync(IDatabase db, Guid tenantId)
    {
        var value = await db.StringGetAsync(VersionKey(tenantId));
        if (value.IsNullOrEmpty)
            return 0;
        return value.TryParse(out long ver) ? ver : 0;
    }

    public async Task<IReadOnlyList<string>?> GetPermissionsAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var version = await ReadVersionAsync(db, tenantId);
            var value = await db.StringGetAsync(UserKey(tenantId, version, userId));
            if (value.IsNullOrEmpty)
                return null; // cache miss → caller resolves from DB

            var perms = JsonSerializer.Deserialize<string[]>((string)value!);
            return perms; // null on a null literal → treated as a miss by the caller
        }
        catch (Exception ex)
        {
            // FAIL OPEN: a Redis error or parse failure is a cache miss, never a throw. The caller then resolves
            // from the DB — a cache blip must never deny or serve stale perms on a version-read failure.
            _logger.LogWarning(
                ex, "Permission-cache read failed for user {UserId} in tenant {TenantId}; treating as cache miss.",
                userId, tenantId);
            return null;
        }
    }

    public async Task SetPermissionsAsync(
        Guid tenantId, Guid userId, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var version = await ReadVersionAsync(db, tenantId);
            var json = JsonSerializer.Serialize(permissions);
            await db.StringSetAsync(UserKey(tenantId, version, userId), json, PermissionsTtl, When.Always, CommandFlags.None);
        }
        catch (Exception ex)
        {
            // Best-effort write; swallow errors so a Redis blip never surfaces in the resolve path.
            _logger.LogWarning(
                ex, "Permission-cache write failed for user {UserId} in tenant {TenantId}; skipping cache set.",
                userId, tenantId);
        }
    }

    public async Task InvalidateAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var version = await ReadVersionAsync(db, tenantId);
            await db.KeyDeleteAsync(UserKey(tenantId, version, userId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Permission-cache invalidate failed for user {UserId} in tenant {TenantId}; skipping.",
                userId, tenantId);
        }
    }

    public async Task InvalidateTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            // INCR the tenant version so every old-version user key is orphaned (expires via its own 15-min TTL).
            // O(1), no SCAN. Refresh the version key's long TTL so it self-cleans only when the tenant goes idle.
            await db.StringIncrementAsync(VersionKey(tenantId));
            await db.KeyExpireAsync(VersionKey(tenantId), VersionTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Permission-cache tenant-invalidate failed for tenant {TenantId}; skipping.", tenantId);
        }
    }
}
