namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// CR-AUTH-001: a Microsoft Entra identity that has already passed OIDC token validation AND the
/// US-AUTH-013 tenant-isolation guard. Produced by the SSO protocol layer (EntraSsoService) and handed
/// to <see cref="HRM.Application.Common.Interfaces.IAuthService"/> to match/link/provision the HRM user
/// and mint the application JWT. By the time an instance exists, the login is already authorized — the
/// auth service trusts <see cref="JitAllowed"/> / <see cref="DefaultRole"/> as the protocol layer's
/// allow-list decision.
/// </summary>
public sealed record SsoIdentity
{
    /// <summary>Resolved HRM tenant subdomain (from the signed OIDC state).</summary>
    public required string Subdomain { get; init; }

    /// <summary>Entra directory id (the validated <c>tid</c> claim) — for auditing.</summary>
    public required string TenantId { get; init; }

    /// <summary>Entra immutable object id (the <c>oid</c> claim) — the primary user match key.</summary>
    public required string ObjectId { get; init; }

    /// <summary>Verified email from the id_token — the fallback match / account-linking key.</summary>
    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>True when the protocol layer's allow-list permits just-in-time provisioning for this
    /// user's domain. When false, a non-member is rejected rather than auto-created.</summary>
    public bool JitAllowed { get; init; }

    /// <summary>Built-in role name to assign to a JIT-provisioned membership (e.g. "Employee").</summary>
    public string? DefaultRole { get; init; }

    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}
