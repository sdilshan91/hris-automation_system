namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Response from MFA enrollment containing the TOTP secret, QR code, and recovery codes.
/// Recovery codes are shown only once during enrollment and cannot be retrieved again.
/// </summary>
public sealed record MfaEnrollResponse
{
    public string Secret { get; init; } = string.Empty;
    public string QrCodeDataUrl { get; init; } = string.Empty;
    public List<string> RecoveryCodes { get; init; } = [];
}
