using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using MediatR;

namespace HRM.Application.Features.Locations.Queries;

/// <summary>
/// Gets a single location by its ID within the current tenant.
/// </summary>
public sealed record GetLocationByIdQuery(
    Guid LocationId
) : IRequest<Result<LocationDto>>;
