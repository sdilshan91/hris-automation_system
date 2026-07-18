using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Authenticated self-service change-password command (US-AUTH-004, ISSUE-248). The acting user id comes from the
/// JWT (never the request body); the current password is verified and the new one is run through the same tenant
/// password-policy + history rules as the token-based reset path.
/// </summary>
public sealed record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result>;
