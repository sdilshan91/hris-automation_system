using HRM.Application.DTOs;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Application.Features.LeaveRequests.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// HR-facing Compulsory-leave / Loss-of-Pay (LOP) endpoints (US-LV-011). All gated with
/// <c>Leave.ManageLop</c> (granted to Tenant Admin / HR Manager / HR Officer). A dedicated
/// controller — following the <see cref="LeaveCarryForwardController"/> precedent for focused
/// leave sub-resources — rather than overloading the self-service <c>LeaveRequestsController</c>.
/// AC-1 (employee confirm-LOP on apply) is handled by the existing <c>POST /api/v1/leaves</c>
/// (CreateLeaveRequest's ConfirmLop flag), not here.
/// </summary>
[ApiController]
[Route("api/v1/leaves")]
[Authorize]
public sealed class LeaveLopController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveLopController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// POST /api/v1/leaves/assign-lop
    /// HR manually assigns LOP days to an employee for the given dates (FR-3, AC-3).
    /// </summary>
    [HttpPost("assign-lop")]
    [RequirePermission("Leave.ManageLop")]
    [ProducesResponseType(typeof(ApiResponse<AssignLopResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignLop(
        [FromBody] AssignLopRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AssignLopCommand(request), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<AssignLopResultDto>.Ok(result.Value!, "LOP assigned."));
    }

    /// <summary>
    /// GET /api/v1/leaves/lop-summary?employeeId={id}&amp;from={date}&amp;to={date}
    /// Returns the LOP entries and total LOP days for an employee over a period, for payroll (FR-5).
    /// </summary>
    [HttpGet("lop-summary")]
    [RequirePermission("Leave.ManageLop")]
    [ProducesResponseType(typeof(ApiResponse<LopSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLopSummary(
        [FromQuery] Guid? employeeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (employeeId is null || from is null || to is null)
            return StatusCode(400, ApiResponse.Fail("employeeId, from, and to are all required."));

        var result = await _mediator.Send(
            new GetLopSummaryQuery(employeeId.Value, from.Value, to.Value), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LopSummaryDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/leaves/compulsory
    /// HR bulk-assigns a compulsory leave (company shutdown) to all/selected employees for the given
    /// dates (FR-6). BR-4: deducts from balance first, then LOP if insufficient. When
    /// <c>EmployeeIds</c> is null/empty the leave applies to all active employees ("Apply to all").
    /// </summary>
    [HttpPost("compulsory")]
    [RequirePermission("Leave.ManageLop")]
    [ProducesResponseType(typeof(ApiResponse<CompulsoryLeaveResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignCompulsoryLeave(
        [FromBody] CompulsoryLeaveRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AssignCompulsoryLeaveCommand(request), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<CompulsoryLeaveResultDto>.Ok(result.Value!, "Compulsory leave assigned."));
    }

    /// <summary>
    /// POST /api/v1/leaves/lop/{id}/override
    /// HR overrides a system-generated LOP entry (BR-3): converts it to a different (balance-backed)
    /// leave type or removes it (when <c>TargetLeaveTypeId</c> is null).
    /// </summary>
    [HttpPost("lop/{id:guid}/override")]
    [RequirePermission("Leave.ManageLop")]
    [ProducesResponseType(typeof(ApiResponse<OverrideLopResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OverrideLop(
        [FromRoute] Guid id,
        [FromBody] OverrideLopRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new OverrideLopCommand(id, request), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<OverrideLopResultDto>.Ok(result.Value!, "LOP entry overridden."));
    }
}
