using HRM.Infrastructure.Multitenancy;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// Production <see cref="ICacheTenantKeyProvider"/>: derives the EF second-level cache-key prefix from the
/// <see cref="AmbientTenant"/> snapshot on the current async flow.
///
/// <para>The library's <c>IEFCacheKeyPrefixProvider</c> is a ROOT singleton, so it cannot resolve the scoped
/// <see cref="HRM.Application.Common.Interfaces.ITenantContext"/>. Previously we bridged that gap for HTTP via
/// <c>IHttpContextAccessor</c>, but background/Hangfire jobs and startup have no <c>HttpContext</c> and so all
/// fell back to a single shared <c>nohttp:</c> prefix — a cross-tenant collision waiting to happen once RLS
/// moves the tenant id out of the SQL. This provider instead reads the <see cref="AsyncLocal{T}"/>-backed
/// ambient tenant, which <c>TenantContext.SetTenant/SetSystemContext</c> publishes on BOTH the HTTP path
/// (<c>TenantResolutionMiddleware</c>) and every Hangfire job. Result: background/job queries now cache under
/// <c>t:{tenantId}:</c> / <c>sys:</c>, never a shared bucket.</para>
/// </summary>
public sealed class AmbientTenantCacheKeyProvider : ICacheTenantKeyProvider
{
    public string GetCacheKeyPrefix() => CacheTenantPrefix.For(AmbientTenant.Current);
}
