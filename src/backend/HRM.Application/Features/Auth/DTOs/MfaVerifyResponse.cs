namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Response from MFA enrollment verification.
/// Recovery codes are included only on first successful verification.
/// </summary>
public sealed record MfaVerifyResponse
{
    public bool Success { get; init; }
    public List<string>? RecoveryCodes { get; init; }
}
