using System.Security.Claims;
using HRM.Application.Common.Interfaces;

namespace HRM.Api.Auth;

/// <summary>
/// P3-2 session-revocation decision for the JWT <c>OnTokenValidated</c> hook, extracted so the exact logic the auth
/// pipeline runs is unit-testable. Reads the validated principal's identity claims (<c>sub</c>, <c>tenant_id</c>,
/// <c>iat</c> — kept 1:1 by <c>MapInboundClaims=false</c>) and asks the <see cref="ITokenDenylist"/> whether the
/// token was issued before the (tenant,user) revocation cutoff.
///
/// <para><b>Fail-open, always.</b> A null principal, a null/unregistered denylist (Redis absent), missing or
/// unparseable claims, or ANY exception thrown by the denylist → returns <c>false</c> (not revoked). A denylist
/// outage must never turn into a mass lockout.</para>
/// </summary>
public static class SessionRevocationCheck
{
    /// <summary>Returns <c>true</c> only when the token is provably revoked; fails open to <c>false</c> otherwise.</summary>
    public static async Task<bool> IsRevokedAsync(
        ClaimsPrincipal? principal, ITokenDenylist? denylist, CancellationToken cancellationToken = default)
    {
        if (principal is null || denylist is null)
            return false;

        var subValue = principal.FindFirst("sub")?.Value;
        var tenantValue = principal.FindFirst("tenant_id")?.Value;
        var iatValue = principal.FindFirst("iat")?.Value;

        if (!Guid.TryParse(subValue, out var userId) ||
            !Guid.TryParse(tenantValue, out var tenantId) ||
            !long.TryParse(iatValue, out var issuedAtUnix))
        {
            return false; // cannot evaluate → fail open
        }

        try
        {
            return await denylist.IsRevokedAsync(userId, tenantId, issuedAtUnix, cancellationToken);
        }
        catch
        {
            return false; // FAIL OPEN: a denylist error can never lock everyone out
        }
    }
}
