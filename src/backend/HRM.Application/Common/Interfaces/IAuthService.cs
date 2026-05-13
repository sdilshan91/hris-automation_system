using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Authentication service interface for login, refresh, logout, and password operations.
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
}
