using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles MFA disablement by delegating to IAuthService.
/// </summary>
public sealed class MfaDisableCommandHandler : IRequestHandler<MfaDisableCommand, Result>
{
    private readonly IAuthService _authService;

    public MfaDisableCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result> Handle(MfaDisableCommand request, CancellationToken cancellationToken)
    {
        return await _authService.DisableMfaAsync(request.UserId, request.TenantId, cancellationToken);
    }
}
