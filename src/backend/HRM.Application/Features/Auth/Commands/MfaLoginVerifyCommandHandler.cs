using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles MFA login challenge verification by delegating to IAuthService.
/// </summary>
public sealed class MfaLoginVerifyCommandHandler : IRequestHandler<MfaLoginVerifyCommand, Result<LoginResponse>>
{
    private readonly IAuthService _authService;

    public MfaLoginVerifyCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<LoginResponse>> Handle(MfaLoginVerifyCommand request, CancellationToken cancellationToken)
    {
        return await _authService.VerifyMfaLoginAsync(
            request.Email,
            request.Code,
            request.IpAddress,
            request.UserAgent,
            cancellationToken);
    }
}
