namespace HRM.Infrastructure.Caching;

/// <summary>
/// P3 (EF second-level cache) — resolves the current request's tenant-scoped cache-key prefix. Registered as a
/// singleton (the library's <c>IEFCacheKeyPrefixProvider</c> is a root singleton), so it must reach the CURRENT
/// request's scoped <c>ITenantContext</c> indirectly (via <c>IHttpContextAccessor</c>) rather than resolving the
/// scoped service off the root container. Extracted behind an interface so tests can drive the prefix
/// deterministically without spinning up an HTTP request.
/// </summary>
public interface ICacheTenantKeyProvider
{
    /// <summary>Returns the never-shared cache-key prefix for the current tenant context (see <see cref="CacheTenantPrefix"/>).</summary>
    string GetCacheKeyPrefix();
}
