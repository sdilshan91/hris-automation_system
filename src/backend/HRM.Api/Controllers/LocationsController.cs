using HRM.Application.DTOs;
using HRM.Application.Features.Locations.Commands;
using HRM.Application.Features.Locations.DTOs;
using HRM.Application.Features.Locations.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped location management endpoints (US-CHR-007).
/// Requires Location.View or Location.Create/Edit/Deactivate permissions.
/// </summary>
[ApiController]
[Route("api/v1/tenant/locations")]
[Authorize]
public sealed class LocationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LocationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/locations
    /// Lists all locations for the current tenant.
    /// Optional query parameter: activeOnly (bool).
    /// </summary>
    [HttpGet]
    [RequirePermission("Location.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LocationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocations(
        [FromQuery] bool? activeOnly, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLocationsQuery(activeOnly), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<LocationDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/locations/{id}
    /// Gets a single location by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Location.View")]
    [ProducesResponseType(typeof(ApiResponse<LocationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLocationById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLocationByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LocationDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/locations
    /// Creates a new location (AC-2).
    /// </summary>
    [HttpPost]
    [RequirePermission("Location.Create")]
    [ProducesResponseType(typeof(ApiResponse<LocationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateLocation(
        [FromBody] CreateLocationRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateLocationCommand(
            request.Name, request.AddressLine1, request.AddressLine2,
            request.City, request.StateProvince, request.Country,
            request.PostalCode, request.TimeZone, request.Phone);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetLocationById),
            new { id = result.Value!.Id },
            ApiResponse<LocationDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/locations/{id}
    /// Updates an existing location.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Location.Edit")]
    [ProducesResponseType(typeof(ApiResponse<LocationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLocation(
        Guid id, [FromBody] UpdateLocationRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateLocationCommand(
            id, request.Name, request.AddressLine1, request.AddressLine2,
            request.City, request.StateProvince, request.Country,
            request.PostalCode, request.TimeZone, request.Phone);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LocationDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/locations/{id}/deactivate
    /// Deactivates a location (AC-3, FR-5).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("Location.Deactivate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateLocation(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateLocationCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Location deactivated successfully."));
    }
}
