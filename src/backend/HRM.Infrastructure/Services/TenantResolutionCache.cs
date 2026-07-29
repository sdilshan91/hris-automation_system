using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Shared invalidator for the subdomain-keyed tenant-resolution cache written by
/// <c>TenantResolutionMiddleware</c>. Owns the ONE copy of the key format
/// (<c>t:subdomain:{subdomain}</c>, subdomain lowercased) so lifecycle transitions (US-ADM-004), the plan-edit
/// sweep (ISSUE-342) and the tenant plan-change endpoint (ISSUE-341) cannot drift apart on the key.
///
/// <para><see cref="IDistributedCache"/> is the SAME optional Redis seam used across the codebase: it is only
/// registered when a Redis connection string is configured, so the parameter is optional and null in tests /
/// no-Redis hosts, in which case every method is a no-op. Every call is best-effort — a miss/outage is logged
/// at Warning and swallowed; the middleware falls back to the database within the TTL window.</para>
/// </summary>
public sealed class TenantResolutionCache : ITenantResolutionCache
{
    // MUST stay in sync with TenantResolutionMiddleware.TenantCacheKeyPrefix.
    private const string TenantCacheKeyPrefix = "t:subdomain:";

    private readonly IDistributedCache? _cache;
    private readonly ILogger<TenantResolutionCache> _logger;

    public TenantResolutionCache(ILogger<TenantResolutionCache> logger, IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
    }

    public async Task InvalidateAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        if (_cache is null || string.IsNullOrWhiteSpace(subdomain))
            return;

        var cacheKey = TenantCacheKeyPrefix + subdomain.ToLowerInvariant();
        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to invalidate tenant resolution cache for {Subdomain}; it will expire on TTL.", subdomain);
        }
    }

    public async Task InvalidateManyAsync(
        IEnumerable<string> subdomains, CancellationToken cancellationToken = default)
    {
        if (_cache is null)
            return;

        // Per-key best-effort: one failing key must not abandon the rest of the sweep.
        foreach (var subdomain in subdomains)
            await InvalidateAsync(subdomain, cancellationToken);
    }
}
