namespace HRM.Infrastructure.Multitenancy;

/// <summary>
/// A readonly snapshot of the ambient tenant carried on the current async flow. Distinct from the scoped
/// <c>ITenantContext</c> (which holds the full tenant profile): this is the minimal identity needed by
/// ROOT-SINGLETON collaborators that cannot resolve the scoped service (e.g. the EF second-level cache
/// key-prefix provider).
/// </summary>
public readonly record struct AmbientTenantInfo(Guid TenantId, bool IsSystemContext, bool IsResolved);

/// <summary>
/// P3/RLS increment 1 — an <see cref="AsyncLocal{T}"/>-backed ambient tenant accessor.
///
/// <para><b>Why static:</b> the load-bearing consumer is the EF second-level cache key-prefix provider, which
/// the library instantiates as a ROOT singleton. A singleton cannot see the request/job's scoped
/// <c>ITenantContext</c>. Previously that gap was bridged for HTTP via <c>IHttpContextAccessor</c>, but
/// background/Hangfire jobs and startup have no <c>HttpContext</c>, so the cache fell back to a single shared
/// <c>nohttp:</c> prefix across all tenants' background work. This static accessor is reachable from the
/// singleton and, because it is <see cref="AsyncLocal{T}"/>-backed, its value flows down the current async
/// context — so it reflects whichever tenant the current request OR job established.</para>
///
/// <para><b>Who publishes to it:</b> the production <c>TenantContext.SetTenant</c> / <c>SetSystemContext</c>.
/// Both the HTTP path (<c>TenantResolutionMiddleware</c>) and every Hangfire job already call one of those at
/// the top of their scope, so the ambient is populated automatically with no per-job wiring.</para>
///
/// <para>Today (RLS off) this is behaviour-neutral except that background/job cached queries now get a
/// tenant-scoped prefix instead of the shared <c>nohttp:</c> one. Under RLS (increment 2) the tenant id leaves
/// the SQL, at which point this ambient prefix is what keeps the cache from colliding across tenants.</para>
/// </summary>
public static class AmbientTenant
{
    private static readonly AsyncLocal<AmbientTenantInfo?> _current = new();

    /// <summary>The ambient tenant on the current async flow, or <c>null</c> if none has been established.</summary>
    public static AmbientTenantInfo? Current => _current.Value;

    /// <summary>Publishes a resolved tenant as the ambient for the current async flow.</summary>
    public static void SetTenant(Guid tenantId) =>
        _current.Value = new AmbientTenantInfo(tenantId, IsSystemContext: false, IsResolved: true);

    /// <summary>Publishes the system/admin (cross-tenant) context as the ambient for the current async flow.</summary>
    public static void SetSystem() =>
        _current.Value = new AmbientTenantInfo(Guid.Empty, IsSystemContext: true, IsResolved: true);

    /// <summary>Clears the ambient for the current async flow (back to the unresolved fallback).</summary>
    public static void Clear() => _current.Value = null;
}
