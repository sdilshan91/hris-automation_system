namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Request payload for MFA enrollment verification (6-digit TOTP code).
/// </summary>
public sealed record MfaVerifyRequest(string Code);
