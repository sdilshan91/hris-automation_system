namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Request payload for MFA challenge during login.
/// Accepts either a 6-digit TOTP code or a recovery code (8-10 characters).
/// </summary>
public sealed record MfaLoginVerifyRequest(string Code, string Email);
