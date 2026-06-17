using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Queries;

/// <summary>US-ADM-006 FR-1: read the current tenant's full company-settings snapshot.</summary>
public sealed record GetTenantSettingsQuery() : IRequest<Result<TenantSettingsDto>>;

public sealed class GetTenantSettingsQueryHandler
    : IRequestHandler<GetTenantSettingsQuery, Result<TenantSettingsDto>>
{
    private readonly ITenantSettingsService _service;

    public GetTenantSettingsQueryHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<TenantSettingsDto>> Handle(GetTenantSettingsQuery request, CancellationToken cancellationToken)
        => _service.GetSettingsAsync(cancellationToken);
}
