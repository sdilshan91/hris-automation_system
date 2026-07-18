using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Queries;

/// <summary>
/// ISSUE-204: read a stored branding asset (logo / email-logo) back as bytes for the public serving endpoints.
/// Tenant-scoped via ITenantContext (resolved from the subdomain before auth), so it is safe to expose
/// anonymously — the logo is public branding shown on the pre-auth login page.
/// </summary>
public sealed record GetBrandingAssetQuery(BrandingAssetKind Kind) : IRequest<Result<BrandingAssetContentDto>>;

public sealed class GetBrandingAssetQueryHandler
    : IRequestHandler<GetBrandingAssetQuery, Result<BrandingAssetContentDto>>
{
    private readonly ITenantSettingsService _service;

    public GetBrandingAssetQueryHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<BrandingAssetContentDto>> Handle(GetBrandingAssetQuery request, CancellationToken cancellationToken)
        => _service.GetBrandingAssetAsync(request.Kind, cancellationToken);
}
