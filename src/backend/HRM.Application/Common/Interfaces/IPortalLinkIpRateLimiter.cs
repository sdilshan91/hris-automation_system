namespace HRM.Application.Common.Interfaces;

/// <summary>
/// DF-7 (US-REC-008 NFR-6, ISSUE-130): the per-IP anti-abuse throttle for applicant-portal magic-link issuance.
/// Caps how many portal tokens a single client IP may mint (across ALL applicant emails) within a rolling window —
/// catching enumeration / provider-exhaustion via rotating emails that the per-EMAIL guard misses.
///
/// <para>Two implementations behind this one seam (mirrors the token-denylist Redis-or-fallback branch in
/// <c>DependencyInjection.AddInfrastructure</c>):</para>
/// <list type="bullet">
///   <item><b>Redis</b> (<c>RedisPortalLinkIpRateLimiter</c>) — a distributed fixed-window INCR counter, so the cap
///     holds across ALL API instances at multi-instance scale (a per-instance count under-counts by N).</item>
///   <item><b>DB fallback</b> (<c>DbCountPortalLinkIpRateLimiter</c>) — the original single-instance sliding-window
///     COUNT over <c>applicant_portal_token</c>, used when Redis is not configured.</item>
/// </list>
///
/// <para>Keying is per-(tenant, IP): the Redis key embeds the tenant id and the DB count runs under the tenant
/// global query filter, so one tenant's traffic never counts against another's.</para>
///
/// <para><b>Fail-open (Redis path).</b> This is an anti-abuse OPTIMISATION, not a security boundary. The
/// <c>RedisPortalLinkIpRateLimiter</c> fails open — a Redis blip degrades to ALLOW rather than locking out legitimate
/// applicants. The DB fallback does not need its own fail-open: if its <c>COUNT</c> throws the database is down, and
/// the token <c>INSERT</c> that immediately follows a successful check would fail anyway, so issuance does not proceed
/// regardless (the same behaviour as the original inline throttle).</para>
/// </summary>
public interface IPortalLinkIpRateLimiter
{
    /// <summary>
    /// Records one issuance attempt for <paramref name="ip"/> in <paramref name="tenantId"/> and reports whether the
    /// caller is still within the cap.
    /// </summary>
    /// <returns>
    /// <c>true</c> to ALLOW the issuance (the caller is at/under the cap for the window); <c>false</c> to BLOCK it
    /// (the caller is OVER the cap → the service should return the 429 <c>rate_limited</c> result). The Redis impl
    /// returns <c>true</c> (fail-open) on a Redis error; see the type remarks for the DB fallback.
    /// </returns>
    Task<bool> TryAcquireAsync(Guid tenantId, string ip, CancellationToken ct = default);
}
