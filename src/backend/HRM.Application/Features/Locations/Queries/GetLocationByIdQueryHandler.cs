using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using MediatR;

namespace HRM.Application.Features.Locations.Queries;

public sealed class GetLocationByIdQueryHandler
    : IRequestHandler<GetLocationByIdQuery, Result<LocationDto>>
{
    private readonly ILocationService _locationService;

    public GetLocationByIdQueryHandler(ILocationService locationService)
    {
        _locationService = locationService;
    }

    public Task<Result<LocationDto>> Handle(
        GetLocationByIdQuery request, CancellationToken cancellationToken)
    {
        return _locationService.GetByIdAsync(request.LocationId, cancellationToken);
    }
}
