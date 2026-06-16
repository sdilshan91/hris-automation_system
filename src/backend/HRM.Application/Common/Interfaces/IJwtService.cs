using HRM.Domain.Entities;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// JWT service interface for token generation and validation.
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Generates a JWT access token with the specified claims.
    /// </summary>
    string GenerateAccessToken(User user, Guid tenantId, Guid userTenantId, IEnumerable<string> roles, IEnumerable<string> permissions);

    /// <summary>
    /// US-ADM-003 (AC-1/FR-1/FR-2/NFR-2): mints a SEPARATE impersonation access token for the TARGET tenant user.
    /// <para>It carries <c>sub</c> = target user id, <c>tenant_id</c> = target tenant, the target user's roles and
    /// permissions within that tenant, <c>is_impersonation</c> = "true", and the <c>imp_*</c> claims (session id,
    /// actor id, truncated reason, read-only flag, expiry). The TTL is <b>capped at 60 minutes</b> (NFR-2) and the
    /// token is NOT refreshable. A separate token type, never a modified copy of the user's own token (§10).</para>
    /// </summary>
    /// <param name="targetUser">The tenant user being impersonated (becomes <c>sub</c>/<c>email</c>).</param>
    /// <param name="targetTenantId">The tenant whose workspace is being operated.</param>
    /// <param name="targetUserTenantId">The target user's membership id within the target tenant.</param>
    /// <param name="roles">The target user's roles within the target tenant.</param>
    /// <param name="permissions">The target user's permissions within the target tenant.</param>
    /// <param name="sessionId">The impersonation session id.</param>
    /// <param name="impersonatorUserId">The System Admin / System Support user driving the session.</param>
    /// <param name="reason">The mandatory reason (truncated for the claim).</param>
    /// <param name="isReadOnly">Whether the session is read-only (System Support or suspended tenant).</param>
    /// <param name="expiresAt">The session hard-expiry; the token's <c>exp</c> = min(expiresAt, now + 60 min).</param>
    string GenerateImpersonationToken(
        User targetUser,
        Guid targetTenantId,
        Guid targetUserTenantId,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        Guid sessionId,
        Guid impersonatorUserId,
        string reason,
        bool isReadOnly,
        DateTime expiresAt);

    /// <summary>
    /// Generates a cryptographically secure refresh token string.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Computes the SHA-256 hash of a refresh token for storage.
    /// </summary>
    string HashToken(string token);

    /// <summary>
    /// Validates a JWT access token and returns the claims principal.
    /// Returns null if the token is invalid.
    /// </summary>
    System.Security.Claims.ClaimsPrincipal? ValidateAccessToken(string token);
}
