using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles admin session revocation (US-AUTH-009 AC-5).
/// If SessionId is provided, revokes that specific session.
/// If SessionId is null, revokes all sessions for the user.
/// </summary>
public sealed class AdminRevokeSessionsCommandHandler : IRequestHandler<AdminRevokeSessionsCommand, Result>
{
    private readonly IAuthService _authService;

    public AdminRevokeSessionsCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result> Handle(AdminRevokeSessionsCommand request, CancellationToken cancellationToken)
    {
        if (request.SessionId.HasValue)
        {
            return await _authService.RevokeSessionAsync(
                request.SessionId.Value,
                request.TargetUserId,
                request.TenantId,
                currentSessionId: null,
                isAdminAction: true,
                cancellationToken);
        }

        // Revoke all sessions
        return await _authService.RevokeAllSessionsAsync(
            request.TargetUserId,
            request.TenantId,
            cancellationToken);
    }
}
