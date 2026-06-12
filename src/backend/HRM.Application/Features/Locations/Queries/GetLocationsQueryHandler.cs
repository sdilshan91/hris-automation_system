using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using MediatR;

namespace HRM.Application.Features.Locations.Queries;

public sealed class GetLocationsQueryHandler
    : IRequestHandler<GetLocationsQuery, Result<IReadOnlyList<LocationDto>>>
{
    private readonly ILocationService _locationService;

    public GetLocationsQueryHandler(ILocationService locationService)
    {
        _locationService = locationService;
    }

    public Task<Result<IReadOnlyList<LocationDto>>> Handle(
        GetLocationsQuery request, CancellationToken cancellationToken)
    {
        return _locationService.GetAllAsync(request.ActiveOnly, cancellationToken);
    }
}
