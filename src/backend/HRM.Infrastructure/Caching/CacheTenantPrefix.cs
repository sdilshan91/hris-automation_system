using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// P3 (EF second-level cache) — the LOAD-BEARING tenant-isolation primitive for the query-result cache.
///
/// <para>The <c>EFCoreSecondLevelCacheInterceptor</c> library caches query results keyed by a hash of the
/// SQL + parameters, but it does NOT isolate tenants. Today the EF global query filter injects
/// <c>WHERE tenant_id = @p</c> into every tenant-scoped query, so the parameter alone already differentiates
/// tenants in the cache key. Once RLS is enabled (a separate effort) the tenant id moves to a session variable
/// and LEAVES the SQL/parameters — at which point identical SQL across tenants would collide. This prefix is
/// therefore designed to be <b>independent of whether the tenant id appears in the SQL</b>: it is derived
/// purely from <see cref="ITenantContext"/>, so it keeps the cache tenant-safe both before and after RLS.</para>
///
/// <para>Distinct, never-shared buckets:
/// <list type="bullet">
///   <item><c>t:{tenantId}:</c> — a resolved tenant (the normal request path).</item>
///   <item><c>sys:</c> — the system/admin context (cross-tenant platform reads).</item>
///   <item><c>none:</c> — resolved-but-empty / unresolved tenant (its own bucket; never shares with a real tenant).</item>
///   <item><c>nohttp:</c> — no request scope (background job / startup / migration); see the OUT-OF-LANE note in
///   <see cref="HttpContextCacheTenantKeyProvider"/> — under RLS this path needs an ambient per-tenant prefix.</item>
/// </list></para>
/// </summary>
public static class CacheTenantPrefix
{
    public const string System = "sys:";
    public const string Unresolved = "none:";
    public const string NoHttpContext = "nohttp:";

    /// <summary>Maps the current tenant context to a never-shared cache-key prefix.</summary>
    public static string For(ITenantContext? tenantContext)
    {
        if (tenantContext is null)
        {
            // No request scope was available to resolve the tenant (background job / startup / migration).
            return NoHttpContext;
        }

        if (tenantContext.IsSystemContext)
        {
            return System;
        }

        if (!tenantContext.IsResolved || tenantContext.TenantId == Guid.Empty)
        {
            return Unresolved;
        }

        return $"t:{tenantContext.TenantId}:";
    }
}
