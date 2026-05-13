using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command for user login with email/password and optional MFA code.
/// </summary>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? MfaCode,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<LoginResponse>>;
