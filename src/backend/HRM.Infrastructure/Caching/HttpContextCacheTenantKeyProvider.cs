using HRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// Production <see cref="ICacheTenantKeyProvider"/>: derives the EF second-level cache-key prefix from the
/// CURRENT request's tenant context.
///
/// <para>The library's <c>IEFCacheKeyPrefixProvider</c> is a ROOT singleton, so we cannot resolve the scoped
/// <see cref="ITenantContext"/> off the container passed to <c>UseCacheKeyPrefix</c> (that would bind to the
/// root scope and never reflect the request's tenant, and throws under scope validation). Instead we reach the
/// live request scope through <see cref="IHttpContextAccessor"/> (AsyncLocal-backed) and pull the scoped
/// <see cref="ITenantContext"/> off <c>HttpContext.RequestServices</c> — the exact instance the DbContext used
/// for its global query filter, so the prefix and the filtered rows always agree on the same tenant.</para>
///
/// <para><b>Background jobs / non-HTTP flows have no <c>HttpContext</c></b>, so this returns
/// <see cref="CacheTenantPrefix.NoHttpContext"/>. Today (RLS off) that is safe because the tenant id is still in
/// every query's parameters, so cache keys cannot collide across tenants. Under RLS this path would need an
/// ambient per-tenant prefix — flagged as an OUT-OF-LANE GAP for the RLS effort.</para>
/// </summary>
public sealed class HttpContextCacheTenantKeyProvider : ICacheTenantKeyProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCacheTenantKeyProvider(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string GetCacheKeyPrefix()
    {
        var tenantContext = _httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();
        return CacheTenantPrefix.For(tenantContext);
    }
}
