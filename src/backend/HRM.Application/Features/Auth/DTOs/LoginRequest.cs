namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Login request payload.
/// </summary>
public sealed record LoginRequest(
    string Email,
    string Password,
    string? MfaCode = null
);
