using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// <see cref="IMyTenantsCache"/> backed by <see cref="IDistributedCache"/> (Redis when configured, else the
/// in-memory distributed cache — so this works in both modes). Removes a user's my-tenants entry
/// (<see cref="MyTenantsCacheKey"/>) on a membership/role change (BUG-116).
///
/// <para><b>Fail-soft by design.</b> A Redis exception is logged and swallowed — a cache blip must NEVER break
/// the membership mutation that triggered the eviction; the stale entry then expires on its own 5-min TTL
/// (mirrors the BUG-121 fail-soft cache handling in <c>AuthService.GetMyTenantsAsync</c>).</para>
/// </summary>
public sealed class MyTenantsCache : IMyTenantsCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<MyTenantsCache> _logger;

    public MyTenantsCache(IDistributedCache cache, ILogger<MyTenantsCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(MyTenantsCacheKey.For(userId), cancellationToken);
        }
        catch (Exception ex)
        {
            // FAIL-SOFT: never let a cache outage break the membership/role mutation; the stale entry expires on TTL.
            _logger.LogWarning(ex,
                "my-tenants cache invalidation failed for user {UserId}; entry will expire on its TTL", userId);
        }
    }
}
