namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Invalidates the subdomain-keyed tenant-resolution cache that <c>TenantResolutionMiddleware</c> writes
/// (key <c>t:subdomain:{subdomain}</c>, TTL <c>Platform:TenantCacheTtlMinutes</c>, default 5 min). The cached
/// entry carries the tenant's <c>EnabledModules</c>/status/branding, so any change to those must drop the entry
/// or the middleware serves a stale snapshot until the TTL elapses.
///
/// <para>Extracted from the private helper that lived on <c>TenantLifecycleService</c> (which invalidated on
/// suspend/terminate/reactivate/restore) so the plan-edit sweep (ISSUE-342) and the tenant plan-change endpoint
/// (ISSUE-341) reuse the SAME key format and best-effort semantics rather than re-deriving the key string. The
/// key format MUST stay in sync with <c>TenantResolutionMiddleware.TenantCacheKeyPrefix</c>.</para>
///
/// <para>Best-effort / fail-open: a cache miss or Redis outage is swallowed — the worst case is the middleware
/// re-reads from the database within the TTL window. Never throws.</para>
/// </summary>
public interface ITenantResolutionCache
{
    /// <summary>Drops the cached resolution entry for one tenant subdomain. No-op when no cache is configured.</summary>
    Task InvalidateAsync(string subdomain, CancellationToken cancellationToken = default);

    /// <summary>Drops the cached resolution entries for many tenant subdomains (the ISSUE-342 plan-edit sweep).</summary>
    Task InvalidateManyAsync(IEnumerable<string> subdomains, CancellationToken cancellationToken = default);
}
