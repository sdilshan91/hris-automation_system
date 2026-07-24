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
    /// CR-AUTH-001 (US-AUTH-014): completes a Microsoft SSO login for an already-validated, already-isolation-
    /// checked <see cref="SsoIdentity"/>. Resolves the tenant, matches the user by Entra <c>oid</c> (then email,
    /// linking the oid), optionally just-in-time provisions a membership with the default role, and mints the
    /// application JWT (+ refresh) by reusing the same issuance path as local login.
    /// </summary>
    Task<Result<LoginResponse>> SsoSignInAsync(SsoIdentity identity, CancellationToken cancellationToken = default);
    Task<Result<RefreshTokenResponse>> RefreshTokenAsync(string refreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<Result> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);
    Task<Result> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);
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

    // Account lockout management (US-AUTH-010)
    Task<Result> UnlockUserAsync(Guid userId, Guid tenantId, Guid adminUserId, CancellationToken cancellationToken = default);

    // Session management (US-AUTH-009)
    Task<Result<IReadOnlyList<SessionDto>>> GetUserSessionsAsync(Guid userId, Guid tenantId, Guid? currentSessionId, CancellationToken cancellationToken = default);
    Task<Result> RevokeSessionAsync(Guid sessionId, Guid userId, Guid tenantId, Guid? currentSessionId, bool isAdminAction, CancellationToken cancellationToken = default);
    Task<Result> UpdateSessionActivityAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<Guid?> GetSessionIdFromTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
