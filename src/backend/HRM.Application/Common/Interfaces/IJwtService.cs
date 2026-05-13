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
