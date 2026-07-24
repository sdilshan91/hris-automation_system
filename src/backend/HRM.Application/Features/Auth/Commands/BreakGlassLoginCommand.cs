using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// US-AUTH-016 (AC-2/AC-7): the distinct break-glass login path — local email/password sign-in reserved for a
/// designated break-glass admin, permitted even under <c>sso_only</c> enforcement. Same shape as
/// <see cref="LoginCommand"/>; routed to <see cref="Common.Interfaces.IAuthService.BreakGlassLoginAsync"/>.
/// </summary>
public sealed record BreakGlassLoginCommand(
    string Email,
    string Password,
    string? MfaCode,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<LoginResponse>>;
