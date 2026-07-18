using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Queries;

/// <summary>
/// DF-29: read a specific tenant's LOGO bytes by <paramref name="Subdomain"/> for the public tenant-switcher.
/// The switcher lists OTHER tenants the user belongs to, so the subdomain-resolved own-tenant serve endpoint
/// cannot serve them; this cross-tenant-by-design route resolves the tenant by exact subdomain match and streams
/// only its logo. Anonymous-safe — the logo is public pre-auth branding shown on each tenant's login page.
/// </summary>
public sealed record GetPublicTenantLogoQuery(string Subdomain) : IRequest<Result<BrandingAssetContentDto>>;

public sealed class GetPublicTenantLogoQueryHandler
    : IRequestHandler<GetPublicTenantLogoQuery, Result<BrandingAssetContentDto>>
{
    private readonly ITenantSettingsService _service;

    public GetPublicTenantLogoQueryHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<BrandingAssetContentDto>> Handle(GetPublicTenantLogoQuery request, CancellationToken cancellationToken)
        => _service.GetPublicTenantLogoAsync(request.Subdomain, cancellationToken);
}
