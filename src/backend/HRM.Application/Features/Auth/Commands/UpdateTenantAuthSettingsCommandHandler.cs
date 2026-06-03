using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles tenant auth settings update by delegating to IAuthService.
/// </summary>
public sealed class UpdateTenantAuthSettingsCommandHandler : IRequestHandler<UpdateTenantAuthSettingsCommand, Result>
{
    private readonly IAuthService _authService;

    public UpdateTenantAuthSettingsCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result> Handle(UpdateTenantAuthSettingsCommand request, CancellationToken cancellationToken)
    {
        return await _authService.UpdateTenantAuthSettingsAsync(
            request.TenantId,
            request.Request,
            cancellationToken);
    }
}
