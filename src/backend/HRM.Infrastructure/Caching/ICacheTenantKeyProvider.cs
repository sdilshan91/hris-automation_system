namespace HRM.Infrastructure.Caching;

/// <summary>
/// P3 (EF second-level cache) — resolves the current tenant-scoped cache-key prefix. Registered as a
/// singleton (the library's <c>IEFCacheKeyPrefixProvider</c> is a root singleton), so it reaches the CURRENT
/// tenant indirectly via the AsyncLocal <c>AmbientTenant</c> (published by <c>TenantContext</c> on both the
/// HTTP and background-job flows) rather than resolving the scoped <c>ITenantContext</c> off the root
/// container. Extracted behind an interface so tests can drive the prefix deterministically.
/// </summary>
public interface ICacheTenantKeyProvider
{
    /// <summary>Returns the never-shared cache-key prefix for the current tenant context (see <see cref="CacheTenantPrefix"/>).</summary>
    string GetCacheKeyPrefix();
}
