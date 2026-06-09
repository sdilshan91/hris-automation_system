using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

public sealed class SwitchTenantCommandHandler
    : IRequestHandler<SwitchTenantCommand, Result<SwitchTenantResponse>>
{
    private readonly IAuthService _authService;

    public SwitchTenantCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public Task<Result<SwitchTenantResponse>> Handle(
        SwitchTenantCommand request,
        CancellationToken cancellationToken)
    {
        return _authService.SwitchTenantAsync(
            request.UserId,
            request.SourceTenantId,
            request.TargetTenantId,
            request.IpAddress,
            request.UserAgent,
            cancellationToken);
    }
}
