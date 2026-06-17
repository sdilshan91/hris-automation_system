namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ONB-005 FR-7 seam: revoke a user's ACTIVE access sessions beyond refresh-token revocation. The story
/// asks for SignalR disconnect + a Redis JWT denylist, neither of which exists in this stack yet. The
/// default implementation is a no-op that logs intent; offboarding completion ALSO revokes the user's
/// refresh tokens via the existing <see cref="IAuthService.RevokeAllSessionsAsync"/> path (which blocks
/// re-auth and refresh). Without the Redis denylist, an already-issued access token remains valid until it
/// expires, because the auth pipeline does not re-check user-active per request.
/// TODO(infra: Redis JWT denylist) — wire a real implementation that adds the user's outstanding access
/// token jti/exp to a Redis deny list and disconnects their SignalR connections.
/// </summary>
public interface ISessionRevoker
{
    /// <summary>
    /// Marks the user's outstanding access tokens for the tenant as revoked (denylist) and disconnects any
    /// live real-time sessions. No-op in the default implementation (the seam is not yet backed by Redis).
    /// </summary>
    Task RevokeActiveSessionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
