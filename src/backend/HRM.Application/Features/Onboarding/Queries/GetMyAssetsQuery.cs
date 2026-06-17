using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>US-ONB-004 AC-4/BR-6: lists the logged-in employee's own assigned assets (read-only).</summary>
public sealed record GetMyAssetsQuery
    : IRequest<Result<IReadOnlyList<AssetDto>>>;

public sealed class GetMyAssetsQueryHandler
    : IRequestHandler<GetMyAssetsQuery, Result<IReadOnlyList<AssetDto>>>
{
    private readonly IAssetService _service;

    public GetMyAssetsQueryHandler(IAssetService service) => _service = service;

    public Task<Result<IReadOnlyList<AssetDto>>> Handle(
        GetMyAssetsQuery request, CancellationToken cancellationToken)
        => _service.GetMineAsync(cancellationToken);
}
