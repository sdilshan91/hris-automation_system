namespace HRM.Domain.Entities;

/// <summary>
/// A magic-link access token issued to a candidate so they can view their application status, interview
/// details, and offer(s) through the anonymous candidate portal (US-REC-008 FR-1/FR-8/BR-6) — without a
/// traditional account. Tenant-scoped via <see cref="BaseEntity.TenantId"/> and the EF global query filter
/// + <c>TenantInterceptor</c>. Maps to the "applicant_portal_token" table.
///
/// The link the candidate receives carries a cryptographically signed token (HMAC-SHA256 over
/// {tenantId|email|expiry} with a server secret, NFR-4). Only the HASH of that token is stored here, never
/// the token itself — presenting a token is validated by re-hashing it and matching this row (plus the
/// signature + expiry checks). This is the same defence-in-depth as password/refresh-token storage.
/// </summary>
public sealed class ApplicantPortalToken : BaseEntity
{
    /// <summary>
    /// The applicant's email this token grants access to (FR-1/BR-6). Lower-cased on issue so lookups are
    /// case-insensitive. Backed by an index on (tenant_id, applicant_email).
    /// </summary>
    public string ApplicantEmail { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash (hex) of the presented token string (NFR-4). The raw token is never stored; validation
    /// re-hashes the presented token and matches this column.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>UTC instant the token expires (FR-8 — default 30 days from issue). After this, access is denied.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// ISSUE-130 (NFR-6): the requester IP captured at issue time, used for the per-IP rate limit that catches
    /// enumeration via rotating emails (the per-email guard alone misses it). Null when no HttpContext (jobs).
    /// </summary>
    public string? RequestIp { get; set; }

    // Issued-at uses the inherited BaseEntity.CreatedAt (stamped by AuditInterceptor / set explicitly on
    // issue). It also drives the basic regeneration rate-limit guard (NFR-6).
}
