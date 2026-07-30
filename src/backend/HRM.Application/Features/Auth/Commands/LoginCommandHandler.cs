using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Observability;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles the LoginCommand by delegating to IAuthService.
/// </summary>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    private readonly IAuthService _authService;

    public LoginCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(
            request.Email,
            request.Password,
            request.MfaCode,
            request.IpAddress,
            request.UserAgent,
            cancellationToken);

        // US-PLT-004 (item 3): login-outcome meter (success|failure). Natural seam — the handler owns the
        // pass/fail decision. No PII tagged (outcome only). Inert when no OTel listener is attached.
        HrmDomainMetrics.RecordLogin(result.IsSuccess);

        return result;
    }
}
