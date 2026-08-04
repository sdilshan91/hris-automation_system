using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Authentication service interface for login, refresh, logout, password, and MFA operations.
/// </summary>
public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(string email, string password, string? mfaCode, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-AUTH-016 FR-2/FR-4 (AC-2/AC-7): the DISTINCT break-glass login path — local email/password sign-in that
    /// is permitted for a DESIGNATED break-glass admin EVEN under <c>sso_only</c> enforcement (bypassing it), and
    /// refused for anyone not designated (BR-2). Has NO external dependency on Entra/the allow-list (NFR-1), so it
    /// works even when SSO is misconfigured or unreachable. A successful break-glass login emits a high-severity
    /// <c>break_glass_login</c> audit event + an admin notification (FR-4/NFR-2). Reuses the same credential and
    /// token-issuance path as <see cref="LoginAsync"/>.
    /// </summary>
    Task<Result<LoginResponse>> BreakGlassLoginAsync(string email, string password, string? mfaCode, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// CR-AUTH-001 (US-AUTH-014): completes a Microsoft SSO login for an already-validated, already-isolation-
    /// checked <see cref="SsoIdentity"/>. Resolves the tenant, matches the user by Entra <c>oid</c> (then email,
    /// linking the oid), optionally just-in-time provisions a membership with the default role, and mints the
    /// application JWT (+ refresh) by reusing the same issuance path as local login.
    /// </summary>
    Task<Result<LoginResponse>> SsoSignInAsync(SsoIdentity identity, CancellationToken cancellationToken = default);
    Task<Result<RefreshTokenResponse>> RefreshTokenAsync(string refreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<Result> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);
    /// <summary>
    /// Resets a password from an emailed link. BUG-295: the TOKEN ALONE identifies the user — the email
    /// parameter was removed because the emailed link never carried it, which is why the flow was dead
    /// end-to-end. The token is 256 bits bound to exactly one user, so the email added no security.
    /// </summary>
    Task<Result> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// BUG-294: redeems a tenant user-invitation — verifies the one-time token, activates the membership with
    /// the invited roles, marks the invitation Accepted, and sets the invitee's first password through the
    /// shared password rail. Anonymous caller; the tenant comes from the subdomain the link lands on.
    /// </summary>
    Task<Result> AcceptInvitationAsync(string token, string newPassword, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default);
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<Result> RevokeAllSessionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<Result<CurrentUserDto>> GetCurrentUserAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<TenantMembershipDto>>> GetMyTenantsAsync(Guid userId, Guid currentTenantId, CancellationToken cancellationToken = default);
    Task<Result<SwitchTenantResponse>> SwitchTenantAsync(Guid userId, Guid sourceTenantId, Guid targetTenantId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    // MFA operations
    Task<Result<MfaEnrollResponse>> EnrollMfaAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<MfaVerifyResponse>> VerifyMfaEnrollmentAsync(Guid userId, string code, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> VerifyMfaLoginAsync(string email, string code, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<Result> DisableMfaAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    // Tenant auth settings
    Task<Result<TenantAuthSettingsResponse>> GetTenantAuthSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Result> UpdateTenantAuthSettingsAsync(Guid tenantId, TenantAuthSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-AUTH-012 FR-8/NFR-1: the per-tenant SSO configuration for the login/callback path, served cache-aside
    /// and invalidated on write. Returns the tenant's current SSO snapshot (disabled defaults if the tenant has
    /// never configured SSO). 404 when the tenant does not exist.
    /// </summary>
    Task<Result<SsoSettingsSnapshot>> GetSsoSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    // ── SSO admin-consent onboarding (US-AUTH-016 FR-5/FR-6) ──────────────────

    /// <summary>
    /// US-AUTH-016 FR-5: marks the tenant's onboarding as <c>consent_pending</c> (the admin has started the
    /// Microsoft admin-consent flow) and returns the tenant's subdomain (needed to sign the consent state).
    /// 404 when the tenant does not exist.
    /// </summary>
    Task<Result<string>> MarkAdminConsentPendingAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-AUTH-016 FR-6/AC-5 (BR-3): records a SUCCESSFUL Microsoft admin consent for the HRM tenant resolved from
    /// the signed state's subdomain — captures the customer's Entra Directory id (<paramref name="customerTid"/>)
    /// into the tenant allow-list (US-AUTH-012 <c>AllowedEntraTenantIds</c>), sets onboarding to <c>consented</c>
    /// (SSO is NOT enabled — the admin must still enable it explicitly), audits <c>sso_admin_consent_completed</c>,
    /// and invalidates the SSO settings cache. Tenant-scoped by subdomain; never enables SSO for another tenant.
    /// </summary>
    Task<Result> CaptureAdminConsentAsync(string subdomain, string customerTid, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-AUTH-016 AC-6: records a FAILED/declined Microsoft admin consent — audits <c>sso_admin_consent_failed</c>
    /// for the tenant resolved from the signed state's subdomain and leaves the prior login mode intact (SSO is not
    /// enabled). Never throws on an unknown tenant (best-effort audit).
    /// </summary>
    Task<Result> RecordAdminConsentFailureAsync(string subdomain, string reason, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-328 (US-AUTH-011 FR-8): records a STRUCTURED audit row for an SSO sign-in/consent FAILURE using the
    /// AC-named event — <c>sso_idp_error</c> (Entra returned an error/deny), <c>sso_token_validation_failed</c>
    /// (code-exchange rejected / id_token invalid / nonce mismatch / missing required claims), or
    /// <c>sso_state_invalid</c> (the signed single-use <c>state</c> could not be validated) — so the failure is
    /// visible in the tenant audit trail (US-NTF-004/005), not only the app log. Tenant attribution: when
    /// <paramref name="subdomain"/> is a TRUSTED source of the HRM tenant (e.g. it came from the signed state
    /// AFTER a successful parse, as for token-validation failures) the row is attributed to that tenant; when it
    /// is null/empty (the state itself could not be validated) a SYSTEM-LEVEL row (null TenantId) is written
    /// rather than trusting an unverified subdomain. Never records tokens/codes/secrets.
    /// </summary>
    Task RecordSsoFailureAsync(string eventType, string? subdomain, string reason, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    // Account lockout management (US-AUTH-010)
    Task<Result> UnlockUserAsync(Guid userId, Guid tenantId, Guid adminUserId, CancellationToken cancellationToken = default);

    // Session management (US-AUTH-009)
    Task<Result<IReadOnlyList<SessionDto>>> GetUserSessionsAsync(Guid userId, Guid tenantId, Guid? currentSessionId, CancellationToken cancellationToken = default);
    Task<Result> RevokeSessionAsync(Guid sessionId, Guid userId, Guid tenantId, Guid? currentSessionId, bool isAdminAction, CancellationToken cancellationToken = default);
    Task<Result> UpdateSessionActivityAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<Guid?> GetSessionIdFromTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
