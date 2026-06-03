using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to verify a TOTP code during the MFA login challenge (step 2 of two-step login).
/// Accepts either a 6-digit TOTP code or a recovery code.
/// </summary>
public sealed record MfaLoginVerifyCommand(
    string Email,
    string Code,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<LoginResponse>>;
