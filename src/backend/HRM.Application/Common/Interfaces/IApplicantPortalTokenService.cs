using HRM.Application.Common.Models;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Issues and validates candidate-portal magic-link tokens (US-REC-008 FR-1/FR-8/NFR-4/BR-6). A token is a
/// self-contained, cryptographically signed string (HMAC-SHA256 over {tenantId|email|expiry} with a server
/// secret) — no session/account is required (BR-6). The token's HASH is persisted in
/// <c>applicant_portal_token</c>; presenting a token is validated by checking the signature + expiry AND
/// matching the stored hash, all scoped to the tenant resolved from the subdomain (AC-4/BR-4).
/// </summary>
public interface IApplicantPortalTokenService
{
    /// <summary>
    /// Issues a magic-link token for the given applicant email in the CURRENT tenant (FR-1). Persists only
    /// the token hash + expiry (NFR-4); returns the raw token (to embed in the link) and its expiry. The
    /// expiry defaults to 30 days (FR-8). Tenant must be resolved.
    /// </summary>
    Task<Result<IssuedPortalToken>> IssueAsync(string applicantEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-REC-008 (BR-5/FR-8): re-issues a magic link for a candidate who entered their email, ONLY if an
    /// application exists for that email in the current tenant (email verification). Anti-enumeration: the
    /// result never reveals whether an application matched — it returns true when an application exists AND a
    /// link was issued, false otherwise; callers surface the same generic message regardless.
    /// </summary>
    Task<Result<bool>> RequestLinkAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a presented token against the CURRENT (subdomain-resolved) tenant (AC-4/BR-4): verifies the
    /// HMAC signature, that the embedded tenant matches the current tenant, that it has not expired (FR-8),
    /// and that a matching, non-expired hash row exists. Returns the applicant email on success; a failure
    /// Result (401) otherwise. A token minted for Tenant A presented on Tenant B's subdomain is rejected.
    /// </summary>
    Task<Result<ValidatedPortalToken>> ValidateAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>The raw magic-link token (to embed in the emailed link) plus its expiry (US-REC-008 FR-1/FR-8).</summary>
public sealed record IssuedPortalToken
{
    /// <summary>The raw, signed token string — embedded in the portal link. Never persisted (only its hash is).</summary>
    public required string Token { get; init; }

    /// <summary>UTC expiry of the token (FR-8).</summary>
    public required DateTime ExpiresAt { get; init; }
}

/// <summary>The resolved identity behind a valid portal token (US-REC-008 AC-4).</summary>
public sealed record ValidatedPortalToken
{
    /// <summary>The applicant email the token grants access to (lower-cased).</summary>
    public required string ApplicantEmail { get; init; }

    /// <summary>The tenant the token is bound to (equals the current tenant — verified during validation).</summary>
    public required Guid TenantId { get; init; }
}
