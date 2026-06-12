using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using MediatR;

namespace HRM.Application.Features.Locations.Commands;

public sealed class UpdateLocationCommandHandler
    : IRequestHandler<UpdateLocationCommand, Result<LocationDto>>
{
    private readonly ILocationService _locationService;

    public UpdateLocationCommandHandler(ILocationService locationService)
    {
        _locationService = locationService;
    }

    public Task<Result<LocationDto>> Handle(
        UpdateLocationCommand request, CancellationToken cancellationToken)
    {
        return _locationService.UpdateAsync(
            request.LocationId, request.Name, request.AddressLine1, request.AddressLine2,
            request.City, request.StateProvince, request.Country,
            request.PostalCode, request.TimeZone, request.Phone,
            cancellationToken);
    }
}
