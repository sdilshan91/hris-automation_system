using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using MediatR;

namespace HRM.Application.Features.Locations.Queries;

/// <summary>
/// Lists all locations for the current tenant as a flat list.
/// Optionally filters by IsActive.
/// </summary>
public sealed record GetLocationsQuery(
    bool? ActiveOnly = null
) : IRequest<Result<IReadOnlyList<LocationDto>>>;
