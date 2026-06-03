using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles MFA enrollment verification by delegating to IAuthService.
/// </summary>
public sealed class MfaVerifyCommandHandler : IRequestHandler<MfaVerifyCommand, Result<MfaVerifyResponse>>
{
    private readonly IAuthService _authService;

    public MfaVerifyCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<MfaVerifyResponse>> Handle(MfaVerifyCommand request, CancellationToken cancellationToken)
    {
        return await _authService.VerifyMfaEnrollmentAsync(request.UserId, request.Code, cancellationToken);
    }
}
