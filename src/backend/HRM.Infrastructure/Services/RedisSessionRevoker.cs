using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Real <see cref="ISessionRevoker"/> (P3-2), registered when Redis is configured (else <see cref="NoOpSessionRevoker"/>).
/// Revoking a user's active sessions writes a per-(tenant,user) revocation cutoff to the Redis-backed
/// <see cref="ITokenDenylist"/>; the <c>OnTokenValidated</c> hook then rejects (401) any access token issued before
/// that cutoff, so offboarding / admin force-logout / impersonation-end invalidate already-issued access tokens
/// instead of leaving them valid until their 15-minute expiry.
///
/// <para><b>SignalR disconnect is intentionally NOT performed here.</b> The security-critical part is the token cutoff
/// (the next HTTP/hub request re-authenticates and is rejected). A clean per-user hub disconnect would need an
/// <c>IHubContext</c> + a user→connection registry that this seam does not own; per the P3-2 contract it is optional
/// and deferred. A live SignalR socket already opened before the revoke stays connected only until its next
/// re-auth or the connection drops.</para>
/// </summary>
public sealed class RedisSessionRevoker : ISessionRevoker
{
    private readonly ITokenDenylist _denylist;
    private readonly ILogger<RedisSessionRevoker> _logger;

    public RedisSessionRevoker(ITokenDenylist denylist, ILogger<RedisSessionRevoker> logger)
    {
        _denylist = denylist;
        _logger = logger;
    }

    public async Task RevokeActiveSessionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _denylist.RevokeAsync(userId, tenantId, cancellationToken);
        _logger.LogInformation(
            "Session-revocation cutoff set for user {UserId} in tenant {TenantId}; access tokens issued before now " +
            "are rejected until their natural expiry.",
            userId, tenantId);
    }
}
