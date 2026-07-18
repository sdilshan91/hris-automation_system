namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Login response with access token, user info, tenant info, and permissions.
/// The refresh token is set as an httpOnly cookie (not in the body).
/// </summary>
public sealed record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public UserDto User { get; init; } = null!;
    public TenantDto Tenant { get; init; } = null!;
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public bool MfaChallenge { get; init; }
    public string? MfaMethod { get; init; }
    public bool MfaEnrollmentRequired { get; init; }

    // US-AUTH-005 AC-7 (ISSUE-241): when a login consumed an MFA recovery code, surface how many unused codes
    // remain and prompt the user to regenerate them once they run low. Null when the login did not burn a
    // recovery code (e.g. password-only or TOTP login).
    public int? RecoveryCodesRemaining { get; init; }
    public bool ShouldRegenerateRecoveryCodes { get; init; }

    // Refresh token is set via cookie, but kept here for internal use
    public string? RefreshToken { get; init; }
}

public sealed record UserDto(Guid UserId, string Email, string? DisplayName);
public sealed record TenantDto(Guid TenantId, string Subdomain, string Name);
