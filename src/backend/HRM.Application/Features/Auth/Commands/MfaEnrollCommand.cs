using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to initiate MFA enrollment for the current authenticated user.
/// No input needed -- the handler uses the current user's identity.
/// </summary>
public sealed record MfaEnrollCommand(Guid UserId) : IRequest<Result<MfaEnrollResponse>>;
