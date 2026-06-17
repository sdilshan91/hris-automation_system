using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>US-ONB-004 AC-4 (HR view): lists the assets currently assigned to a given employee.</summary>
public sealed record GetEmployeeAssetsQuery(Guid EmployeeId)
    : IRequest<Result<IReadOnlyList<AssetDto>>>;

public sealed class GetEmployeeAssetsQueryHandler
    : IRequestHandler<GetEmployeeAssetsQuery, Result<IReadOnlyList<AssetDto>>>
{
    private readonly IAssetService _service;

    public GetEmployeeAssetsQueryHandler(IAssetService service) => _service = service;

    public Task<Result<IReadOnlyList<AssetDto>>> Handle(
        GetEmployeeAssetsQuery request, CancellationToken cancellationToken)
        => _service.GetByEmployeeAsync(request.EmployeeId, cancellationToken);
}
