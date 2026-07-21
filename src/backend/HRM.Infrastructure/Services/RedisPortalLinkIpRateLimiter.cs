using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HRM.Infrastructure.Services;

/// <summary>
/// DF-7: distributed, multi-instance-safe implementation of <see cref="IPortalLinkIpRateLimiter"/>. Backs the per-IP
/// portal-link cap with a Redis fixed-window counter, so the limit holds across ALL API instances (a per-instance
/// DB count under-counts by N and lets N×cap through). Routes through the SHARED <see cref="IConnectionMultiplexer"/>
/// the API host registers (the same instance the P3-2 <see cref="RedisTokenDenylist"/> + <c>RedisPermissionCache</c>
/// use). Registered only when Redis is configured (see <c>DependencyInjection.AddInfrastructure</c>); otherwise the
/// <see cref="DbCountPortalLinkIpRateLimiter"/> sliding-window fallback is used.
///
/// <para><b>Fixed window (INCR + EXPIRE):</b> the first hit in a window <c>INCR</c>s the counter to 1 and sets the
/// window TTL; subsequent hits just <c>INCR</c>. This is a COARSE anti-abuse counter — because the window is fixed
/// (not sliding), a burst straddling a window boundary can allow up to ~2× the cap. That is an acceptable trade for
/// an anti-abuse throttle (vs the DB fallback's precise sliding window), and it keeps the hot path a single O(1)
/// round-trip with no keyspace scan.</para>
///
/// <para><b>Fail-open by design.</b> A Redis error must degrade to ALLOW — never throw, never deny. This is an
/// anti-abuse optimisation, not a security boundary, so a Redis blip must not lock out legitimate applicants.</para>
/// </summary>
public sealed class RedisPortalLinkIpRateLimiter : IPortalLinkIpRateLimiter
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly PortalLinkRateLimitOptions _options;
    private readonly ILogger<RedisPortalLinkIpRateLimiter> _logger;

    public RedisPortalLinkIpRateLimiter(
        IConnectionMultiplexer multiplexer,
        IOptions<PortalLinkRateLimitOptions> options,
        ILogger<RedisPortalLinkIpRateLimiter> logger)
    {
        _multiplexer = multiplexer;
        _options = options.Value;
        _logger = logger;
    }

    // Per-(tenant, IP) key: one tenant's traffic never counts against another's.
    private static string Key(Guid tenantId, string ip) => $"hrm:ratelimit:portal-link:{tenantId}:{ip}";

    public async Task<bool> TryAcquireAsync(Guid tenantId, string ip, CancellationToken ct = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var key = Key(tenantId, ip);

            var n = await db.StringIncrementAsync(key);
            if (n == 1)
            {
                // First hit in this window: stamp the fixed-window TTL so the counter self-expires.
                await db.KeyExpireAsync(key, TimeSpan.FromSeconds(_options.IpWindowSeconds));
            }

            if (n > _options.MaxIssuesPerIp)
            {
                _logger.LogWarning(
                    "Portal token issue throttled (NFR-6, per-IP, Redis) from {RequestIp} in tenant {TenantId} — {Count} issued in the current {Window}s window (cap {Cap}).",
                    ip, tenantId, n, _options.IpWindowSeconds, _options.MaxIssuesPerIp);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // FAIL OPEN: a Redis blip degrades to ALLOW. An anti-abuse throttle must never lock out real applicants.
            _logger.LogWarning(
                ex, "Portal-link per-IP rate-limit check failed for {RequestIp} in tenant {TenantId}; allowing (fail-open).",
                ip, tenantId);
            return true;
        }
    }
}
