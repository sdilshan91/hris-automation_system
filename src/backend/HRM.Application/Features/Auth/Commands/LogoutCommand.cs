using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to log out the current session by revoking the refresh token.
/// </summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest<Result>;
