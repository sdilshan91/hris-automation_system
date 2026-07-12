namespace HRM.Application.Common.Interfaces;

/// <summary>
/// P3-2 JWT access-token denylist (real session revocation). Backs <see cref="ISessionRevoker"/> so that
/// revoking a user's sessions (offboarding US-ONB-005 FR-7, admin force-logout, impersonation end) actually
/// invalidates already-issued ACCESS tokens — which otherwise live until their 15-minute expiry because the
/// auth pipeline never re-checks them.
///
/// <para><b>Design — per-(tenant,user) "revoked-before" cutoff, NOT per-jti enumeration.</b> Revoking sets a
/// single cutoff timestamp; every access token issued (<c>iat</c>) before that cutoff is rejected. The cutoff
/// key carries a TTL of roughly the access-token lifetime + buffer, so it self-cleans once no pre-cutoff token
/// can still be valid.</para>
///
/// <para><b>Fail-open.</b> <see cref="IsRevokedAsync"/> MUST treat any backing-store error, missing key, or parse
/// failure as "not revoked" — a Redis blip can never be allowed to lock every user out. See
/// <c>RedisTokenDenylist</c> (real) and <c>NoOpTokenDenylist</c> (Redis-absent).</para>
/// </summary>
public interface ITokenDenylist
{
    /// <summary>
    /// Records the revocation cutoff for the (tenant, user) as "now": every access token issued before this call
    /// is henceforth rejected until it would have expired anyway. Best-effort — must never throw into the caller.
    /// </summary>
    Task RevokeAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> only when a cutoff exists for the (tenant, user) AND the token's issued-at
    /// (<paramref name="tokenIssuedAtUnix"/>, unix seconds) is strictly before it. Fails OPEN (returns
    /// <c>false</c>) on any error, missing key, or unparseable cutoff.
    /// </summary>
    Task<bool> IsRevokedAsync(Guid userId, Guid tenantId, long tokenIssuedAtUnix, CancellationToken cancellationToken = default);
}
