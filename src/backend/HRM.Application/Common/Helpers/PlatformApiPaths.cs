namespace HRM.Application.Common.Helpers;

/// <summary>
/// The single source of truth for classifying a request path as a <b>metered/gated tenant API path</b> — i.e.
/// a <c>/api/</c> path that is NOT a cross-cutting platform path. Extracted so the two cross-cutting per-request
/// gates that must agree on "which paths count against / are constrained by a tenant's plan" share one predicate
/// instead of drifting apart:
/// <list type="bullet">
///   <item><c>ModuleEntitlementMiddleware</c> — the module-entitlement 403 gate; and</item>
///   <item><c>ApiCallCounterMiddleware</c> — the per-tenant API-call usage counter (US-PLT-004).</item>
/// </list>
///
/// <para>Counting a health probe or the FE bootstrap/login call against a customer's <c>max_api_calls_per_month</c>
/// would look like a billing bug, and gating those paths would lock a tenant's admins out of the console they use
/// to see their plan — so both skips must be identical, which is exactly what this shared list guarantees.</para>
/// </summary>
public static class PlatformApiPaths
{
    // Cross-cutting / platform paths that are NEVER gated and NEVER metered: auth, the FE tenant-context
    // bootstrap, tenant admin surfaces, notifications, and all system/admin controllers. Matched segment-aware
    // (see MatchesSegment) so "/api/v1/auth" matches "/api/v1/auth/login" but not "/api/v1/authz-foo".
    public static readonly string[] PlatformAllowList =
    {
        "/api/v1/auth",                    // login/refresh/logout + /auth/sso
        "/api/v1/tenant/context",          // the FE bootstrap (plan/modules/branding)
        "/api/v1/tenant/settings",
        "/api/v1/tenant/users",
        "/api/v1/tenant/roles",
        "/api/v1/tenant/audit-logs",
        "/api/v1/tenant/workflows",
        "/api/v1/tenant/workflow-instances",
        "/api/v1/tenant/data-exports",     // GDPR export — must survive even a restricted plan
        "/api/v1/notifications",
        "/api/v1/notification-preferences",
        "/api/v1/notification-templates",
        "/api/v1/system",                  // all system/admin controllers
    };

    /// <summary>True when <paramref name="path"/> is on the platform allow-list (segment-aware).</summary>
    public static bool IsPlatformAllowListed(string path)
        => PlatformAllowList.Any(p => MatchesSegment(path, p));

    /// <summary>
    /// True when <paramref name="path"/> is a tenant API path that should be metered/gated: it starts with
    /// <c>/api/</c> AND is not a platform allow-listed path. Non-API paths (health/swagger/hangfire) and the
    /// allow-list return false.
    /// </summary>
    public static bool IsMeteredTenantApiPath(string path)
        => path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !IsPlatformAllowListed(path);

    /// <summary>
    /// Segment-aware prefix match: "/api/v1/leaves" matches itself and "/api/v1/leaves/123" but NOT
    /// "/api/v1/leaves-archive". Raw StartsWith would conflate sibling prefixes.
    /// </summary>
    public static bool MatchesSegment(string path, string prefix)
        => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
