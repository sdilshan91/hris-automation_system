using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles admin-unlock of a user account by delegating to IAuthService.
/// </summary>
public sealed class UnlockUserCommandHandler : IRequestHandler<UnlockUserCommand, Result>
{
    private readonly IAuthService _authService;

    public UnlockUserCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result> Handle(UnlockUserCommand request, CancellationToken cancellationToken)
    {
        return await _authService.UnlockUserAsync(
            request.UserId,
            request.TenantId,
            request.AdminUserId,
            cancellationToken);
    }
}
