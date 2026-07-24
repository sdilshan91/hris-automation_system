using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>Handles <see cref="BreakGlassLoginCommand"/> by delegating to the break-glass login path.</summary>
public sealed class BreakGlassLoginCommandHandler : IRequestHandler<BreakGlassLoginCommand, Result<LoginResponse>>
{
    private readonly IAuthService _authService;

    public BreakGlassLoginCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<LoginResponse>> Handle(BreakGlassLoginCommand request, CancellationToken cancellationToken)
    {
        return await _authService.BreakGlassLoginAsync(
            request.Email,
            request.Password,
            request.MfaCode,
            request.IpAddress,
            request.UserAgent,
            cancellationToken);
    }
}
