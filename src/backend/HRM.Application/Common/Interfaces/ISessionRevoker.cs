namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ONB-005 FR-7 seam: revoke a user's ACTIVE access sessions beyond refresh-token revocation. Backed by a
/// real Redis JWT denylist (P3-2): <c>RedisSessionRevoker</c> writes a per-(tenant,user) "revoked-before"
/// cutoff via <see cref="ITokenDenylist"/>, and the JWT <c>OnTokenValidated</c> hook rejects any access token
/// whose <c>iat</c> predates the cutoff (fail-open on any Redis error). Offboarding completion ALSO revokes the
/// user's refresh tokens via <see cref="IAuthService.RevokeAllSessionsAsync"/>. When Redis is not configured the
/// registration falls back to <c>NoOpSessionRevoker</c>/<c>NoOpTokenDenylist</c> (refresh-token revocation still
/// applies; access tokens then expire naturally within their 15-minute TTL). SignalR per-connection disconnect
/// remains an optional future enhancement (the token cutoff already blocks the next hub re-auth).
/// </summary>
public interface ISessionRevoker
{
    /// <summary>
    /// Marks the user's outstanding access tokens for the tenant as revoked (sets the Redis "revoked-before"
    /// cutoff). No-op when Redis is not configured.
    /// </summary>
    Task RevokeActiveSessionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
