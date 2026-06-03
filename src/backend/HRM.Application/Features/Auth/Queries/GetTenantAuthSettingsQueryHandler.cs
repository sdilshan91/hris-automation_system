using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Queries;

/// <summary>
/// Handles tenant auth settings retrieval by delegating to IAuthService.
/// </summary>
public sealed class GetTenantAuthSettingsQueryHandler
    : IRequestHandler<GetTenantAuthSettingsQuery, Result<TenantAuthSettingsResponse>>
{
    private readonly IAuthService _authService;

    public GetTenantAuthSettingsQueryHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<TenantAuthSettingsResponse>> Handle(
        GetTenantAuthSettingsQuery request,
        CancellationToken cancellationToken)
    {
        return await _authService.GetTenantAuthSettingsAsync(request.TenantId, cancellationToken);
    }
}
