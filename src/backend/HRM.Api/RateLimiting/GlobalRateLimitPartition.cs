using HRM.Application.Common.Interfaces;

namespace HRM.Api.RateLimiting;

/// <summary>
/// GAP-018: how the GLOBAL rate limiter partitions a request.
///
/// <para>Extracted as a pure function for one reason: the partition key IS the control. Every named policy in
/// <c>Program.cs</c> partitions on client IP and is attached to an anonymous endpoint, which left every
/// AUTHENTICATED tenant endpoint unthrottled — and the HTTP integration host sets
/// <c>RateLimiting:Disabled=true</c> (it drives ~12 test classes from one identity), so the limiter cannot be
/// exercised through that harness. A control that the test suite structurally cannot reach is a control on
/// paper; this class is directly testable.</para>
///
/// <para><b>Why (tenantId, userId) and not IP:</b> IP is the wrong identity for authenticated traffic. A whole
/// office behind one NAT shares an address, so an IP partition throttles innocent colleagues together; a single
/// abusive token meanwhile roams across addresses and slips any IP bucket. The tenant is in the key as well so
/// one noisy tenant cannot eat another tenant's allowance.</para>
/// </summary>
public static class GlobalRateLimitPartition
{
    /// <summary>Partition key for traffic that is deliberately not limited.</summary>
    public const string Unlimited = "unlimited";

    /// <summary>Partition key for system (platform-admin) context.</summary>
    public const string SystemContext = "system";

    /// <summary>
    /// Resolves the partition key, or one of the two sentinel keys above when the request is exempt.
    /// </summary>
    /// <param name="path">Request path.</param>
    /// <param name="rateLimitingDisabled">The <c>RateLimiting:Disabled</c> switch.</param>
    /// <param name="tenantContext">Resolved tenant context, if any.</param>
    /// <param name="currentUser">Authenticated user, if any.</param>
    /// <param name="clientIp">Fallback identity for anonymous traffic.</param>
    /// <returns>
    /// <see cref="Unlimited"/> / <see cref="SystemContext"/> for exempt requests, otherwise
    /// <c>t:{tenantId}:u:{userId}</c> when authenticated or <c>ip:{clientIp}</c> when not.
    /// </returns>
    public static string Resolve(
        string? path,
        bool rateLimitingDisabled,
        ITenantContext? tenantContext,
        ICurrentUser? currentUser,
        string clientIp)
    {
        // The test host sets RateLimiting:Disabled; the same flag already exempts auth-login.
        if (rateLimitingDisabled)
        {
            return Unlimited;
        }

        // Infra probes and dev docs: polled by design, outside tenant scope, and /health deliberately
        // bypasses tenant resolution entirely — throttling them would break liveness checks.
        var p = path ?? string.Empty;
        if (p.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return Unlimited;
        }

        // Platform admins run legitimate cross-tenant sweeps from a single identity; bucketing them with a
        // per-user tenant limit would throttle the operator console during normal use.
        if (tenantContext is { IsSystemContext: true })
        {
            return SystemContext;
        }

        // Anonymous traffic has no identity but its address, and those endpoints carry their own tighter
        // named policies (forgot-password 5/h, public-application 10/h, auth-login 10/min) on top of this.
        return currentUser is { IsAuthenticated: true }
            ? $"t:{tenantContext?.TenantId}:u:{currentUser.UserId}"
            : $"ip:{clientIp}";
    }
}
