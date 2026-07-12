using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Redis-backed <see cref="ITokenDenylist"/> (P3-2). Stores a per-(tenant,user) revocation cutoff as a unix-second
/// timestamp under <c>hrm:revoked:{tenantId}:{userId}</c> with a bounded TTL, and rejects any access token whose
/// <c>iat</c> predates that cutoff. Registered only when Redis is configured (see
/// <c>DependencyInjection.AddInfrastructure</c>); otherwise <see cref="NoOpTokenDenylist"/> is used.
///
/// <para><b>Fail-open by design.</b> Every Redis interaction is wrapped so that a connection blip, timeout, missing
/// key, or unparseable value degrades to "not revoked" — a denylist outage must never lock users out. Revocation
/// failures are logged at Error (a session may then live to its natural 15-minute expiry) but never thrown.</para>
/// </summary>
public sealed class RedisTokenDenylist : ITokenDenylist
{
    // Access tokens live 15 minutes (JwtService.GenerateAccessToken AddMinutes(15)); a 16-minute TTL guarantees the
    // cutoff outlives every token issued before the revoke, then self-expires so the key store never grows unbounded.
    private static readonly TimeSpan CutoffTtl = TimeSpan.FromMinutes(16);

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisTokenDenylist> _logger;

    public RedisTokenDenylist(IConnectionMultiplexer multiplexer, ILogger<RedisTokenDenylist> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    private static string Key(Guid tenantId, Guid userId) => $"hrm:revoked:{tenantId}:{userId}";

    public async Task RevokeAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            var db = _multiplexer.GetDatabase();
            // Explicit When/CommandFlags pin this to the (key, value, expiry, when, flags) overload (avoids the
            // ambiguous 3-arg call). SET with a 16-minute TTL so the cutoff self-expires.
            await db.StringSetAsync(Key(tenantId, userId), nowUnix, CutoffTtl, When.Always, CommandFlags.None);
        }
        catch (Exception ex)
        {
            // Do NOT rethrow into offboarding/logout flows. A failed write means the access token may survive to
            // its natural expiry, so log at Error — but the caller's own flow (refresh-token revocation etc.)
            // must still complete.
            _logger.LogError(
                ex, "Failed to write session-revocation cutoff for user {UserId} in tenant {TenantId} to Redis.",
                userId, tenantId);
        }
    }

    public async Task<bool> IsRevokedAsync(
        Guid userId, Guid tenantId, long tokenIssuedAtUnix, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var value = await db.StringGetAsync(Key(tenantId, userId));
            if (value.IsNullOrEmpty)
                return false; // no cutoff recorded → not revoked

            if (!value.TryParse(out long cutoffUnix))
                return false; // unparseable value → fail OPEN

            return tokenIssuedAtUnix < cutoffUnix;
        }
        catch (Exception ex)
        {
            // FAIL OPEN: a Redis error must never lock everyone out. Treat as not revoked and log at Warning.
            _logger.LogWarning(
                ex, "Session-revocation check failed for user {UserId} in tenant {TenantId}; failing open (token treated as not revoked).",
                userId, tenantId);
            return false;
        }
    }
}
