using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>US-ONB-004 FR-3: lists Available assets (optionally filtered by type) for the issuance dropdown.</summary>
public sealed record GetAvailableAssetsQuery(string? AssetType)
    : IRequest<Result<IReadOnlyList<AvailableAssetDto>>>;

public sealed class GetAvailableAssetsQueryHandler
    : IRequestHandler<GetAvailableAssetsQuery, Result<IReadOnlyList<AvailableAssetDto>>>
{
    private readonly IAssetService _service;

    public GetAvailableAssetsQueryHandler(IAssetService service) => _service = service;

    public Task<Result<IReadOnlyList<AvailableAssetDto>>> Handle(
        GetAvailableAssetsQuery request, CancellationToken cancellationToken)
        => _service.GetAvailableAsync(request.AssetType, cancellationToken);
}
