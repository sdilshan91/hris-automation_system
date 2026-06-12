using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Locations.Commands;

public sealed class DeactivateLocationCommandHandler
    : IRequestHandler<DeactivateLocationCommand, Result>
{
    private readonly ILocationService _locationService;

    public DeactivateLocationCommandHandler(ILocationService locationService)
    {
        _locationService = locationService;
    }

    public Task<Result> Handle(
        DeactivateLocationCommand request, CancellationToken cancellationToken)
    {
        return _locationService.DeactivateAsync(request.LocationId, cancellationToken);
    }
}
