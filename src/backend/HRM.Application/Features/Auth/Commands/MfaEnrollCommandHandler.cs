using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles MFA enrollment by delegating to IAuthService.
/// </summary>
public sealed class MfaEnrollCommandHandler : IRequestHandler<MfaEnrollCommand, Result<MfaEnrollResponse>>
{
    private readonly IAuthService _authService;

    public MfaEnrollCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<MfaEnrollResponse>> Handle(MfaEnrollCommand request, CancellationToken cancellationToken)
    {
        return await _authService.EnrollMfaAsync(request.UserId, cancellationToken);
    }
}
