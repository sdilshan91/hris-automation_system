using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using MediatR;

namespace HRM.Application.Features.Locations.Commands;

public sealed class CreateLocationCommandHandler
    : IRequestHandler<CreateLocationCommand, Result<LocationDto>>
{
    private readonly ILocationService _locationService;

    public CreateLocationCommandHandler(ILocationService locationService)
    {
        _locationService = locationService;
    }

    public Task<Result<LocationDto>> Handle(
        CreateLocationCommand request, CancellationToken cancellationToken)
    {
        return _locationService.CreateAsync(
            request.Name, request.AddressLine1, request.AddressLine2,
            request.City, request.StateProvince, request.Country,
            request.PostalCode, request.TimeZone, request.Phone,
            cancellationToken);
    }
}
