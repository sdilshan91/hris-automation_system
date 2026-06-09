using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Authentication service interface for login, refresh, logout, password, and MFA operations.
/// </summary>
public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(string email, string password, string? mfaCode, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<Result<RefreshTokenResponse>> RefreshTokenAsync(string refreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<Result> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);
    Task<Result> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);
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
}
