using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles session revocation by delegating to IAuthService (US-AUTH-009).
/// </summary>
public sealed class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand, Result>
{
    private readonly IAuthService _authService;

    public RevokeSessionCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result> Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        return await _authService.RevokeSessionAsync(
            request.SessionId,
            request.UserId,
            request.TenantId,
            request.CurrentSessionId,
            request.IsAdminAction,
            cancellationToken);
    }
}
