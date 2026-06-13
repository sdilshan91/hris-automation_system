using HRM.Application.DTOs;
using HRM.Application.Features.LeaveTypes.Commands;
using HRM.Application.Features.LeaveTypes.DTOs;
using HRM.Application.Features.LeaveTypes.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped leave type configuration endpoints (US-LV-001).
/// Requires LeaveType.View or LeaveType.Create/Edit/Deactivate permissions.
/// </summary>
[ApiController]
[Route("api/v1/tenant/leave-types")]
[Authorize]
public sealed class LeaveTypesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveTypesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/leave-types
    /// Lists all leave types for the current tenant, ordered by display_order.
    /// Optional query parameter: activeOnly (bool).
    /// </summary>
    [HttpGet]
    [RequirePermission("LeaveType.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LeaveTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeaveTypes(
        [FromQuery] bool? activeOnly, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLeaveTypesQuery(activeOnly), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<LeaveTypeDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/leave-types/{id}
    /// Gets a single leave type by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("LeaveType.View")]
    [ProducesResponseType(typeof(ApiResponse<LeaveTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLeaveTypeById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLeaveTypeByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveTypeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/leave-types
    /// Creates a new leave type (AC-1).
    /// </summary>
    [HttpPost]
    [RequirePermission("LeaveType.Create")]
    [ProducesResponseType(typeof(ApiResponse<LeaveTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateLeaveType(
        [FromBody] CreateLeaveTypeRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateLeaveTypeCommand(
            request.Name, request.Code, request.Color, request.Description,
            request.AnnualEntitlement, request.AccrualFrequency,
            request.CarryForwardLimit, request.CarryForwardExpiryMonths,
            request.ProbationEligible, request.DocumentsRequired, request.DocumentDayThreshold,
            request.Encashable, request.MaxEncashDays,
            request.HalfDayAllowed, request.HourlyAllowed,
            request.Gender, request.MaxConsecutiveDays,
            request.NegativeBalanceAllowed, request.NegativeBalanceLimit,
            request.DisplayOrder);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetLeaveTypeById),
            new { id = result.Value!.Id },
            ApiResponse<LeaveTypeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/leave-types/{id}
    /// Updates an existing leave type (AC-2).
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("LeaveType.Edit")]
    [ProducesResponseType(typeof(ApiResponse<LeaveTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLeaveType(
        Guid id, [FromBody] UpdateLeaveTypeRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateLeaveTypeCommand(
            id, request.Name, request.Code, request.Color, request.Description,
            request.AnnualEntitlement, request.AccrualFrequency,
            request.CarryForwardLimit, request.CarryForwardExpiryMonths,
            request.ProbationEligible, request.DocumentsRequired, request.DocumentDayThreshold,
            request.Encashable, request.MaxEncashDays,
            request.HalfDayAllowed, request.HourlyAllowed,
            request.Gender, request.MaxConsecutiveDays,
            request.NegativeBalanceAllowed, request.NegativeBalanceLimit);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveTypeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/leave-types/{id}/deactivate
    /// Deactivates a leave type (AC-4, FR-5).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("LeaveType.Deactivate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateLeaveType(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateLeaveTypeCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Leave type deactivated successfully."));
    }

    /// <summary>
    /// POST /api/v1/tenant/leave-types/{id}/reactivate
    /// Reactivates a previously deactivated leave type.
    /// </summary>
    [HttpPost("{id:guid}/reactivate")]
    [RequirePermission("LeaveType.Edit")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateLeaveType(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ReactivateLeaveTypeCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Leave type reactivated successfully."));
    }

    /// <summary>
    /// POST /api/v1/tenant/leave-types/reorder
    /// Reorders leave types by display_order (FR-3).
    /// </summary>
    [HttpPost("reorder")]
    [RequirePermission("LeaveType.Edit")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReorderLeaveTypes(
        [FromBody] ReorderLeaveTypesRequest request, CancellationToken cancellationToken)
    {
        var command = new ReorderLeaveTypesCommand(request.LeaveTypeIds);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Leave types reordered successfully."));
    }
}
