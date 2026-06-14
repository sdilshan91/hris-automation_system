using HRM.Application.DTOs;
using HRM.Application.Features.LeaveCarryForward.DTOs;
using HRM.Application.Features.LeaveCarryForward.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// HR-config endpoints for leave carry-forward / expiry rules (US-LV-008).
/// The year-end and monthly-expiry jobs run on Hangfire; this controller exposes only the
/// read-only year-end preview (FR-5, AC-5). Gated with Leave.ConfigurePolicy — the same
/// leave-config permission US-LV-002 entitlement configuration uses.
/// </summary>
[ApiController]
[Route("api/v1/leaves")]
[Authorize]
public sealed class LeaveCarryForwardController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveCarryForwardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/leaves/carry-forward-preview?year={year}
    /// Returns the projected carry-forward and forfeiture per employee × leave type for the
    /// supplied closing leave year (defaults to the current calendar year). Read-only; commits
    /// nothing (FR-5, AC-5). Matches exactly what ProcessLeaveYearEndJob would produce.
    /// </summary>
    [HttpGet("carry-forward-preview")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CarryForwardPreviewDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCarryForwardPreview(
        [FromQuery] int? year, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCarryForwardPreviewQuery(year), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<CarryForwardPreviewDto>>.Ok(result.Value!));
    }
}
