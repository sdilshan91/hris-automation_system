using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to refresh an access token using a refresh token from cookie.
/// </summary>
public sealed record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<RefreshTokenResponse>>;
