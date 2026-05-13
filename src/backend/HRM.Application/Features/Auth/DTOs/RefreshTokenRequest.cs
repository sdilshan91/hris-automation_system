namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Refresh token request. The actual token comes from the httpOnly cookie.
/// </summary>
public sealed record RefreshTokenRequest;

/// <summary>
/// Refresh token response with new access token. New refresh token is set via cookie.
/// </summary>
public sealed record RefreshTokenResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string? RefreshToken { get; init; } // Set via cookie, kept for internal use
}
