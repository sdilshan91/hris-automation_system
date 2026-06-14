using HRM.Application.DTOs;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.Queries;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Employee attendance endpoints (US-ATT-001).
/// Employees clock in from the browser; the punch is tenant-scoped via the resolved tenant
/// context and the acting employee is resolved from the JWT.
/// </summary>
[ApiController]
[Route("api/v1/attendance")]
[Authorize]
public sealed class AttendanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public AttendanceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// POST /api/v1/attendance/clock-in
    /// Clocks the current employee in (FR-1..FR-5, AC-1..AC-5). The source IP, user agent, and the
    /// optional Idempotency-Key header are captured here from the HTTP context and passed to the
    /// command (NFR-4). Coordinates / photo are conditional on the tenant attendance policy.
    /// Gated by Attendance.CheckIn — the self clock-in permission held by the Employee role.
    /// </summary>
    [HttpPost("clock-in")]
    [RequirePermission("Attendance.CheckIn")]
    [ProducesResponseType(typeof(ApiResponse<AttendanceLogDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClockIn(
        [FromBody] ClockInRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.FirstOrDefault();

        var command = new ClockInCommand(
            request.Latitude,
            request.Longitude,
            request.PhotoUrl,
            request.Source,
            ipAddress,
            userAgent,
            idempotencyKey);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<AttendanceLogDto>.Ok(result.Value!, "Clocked in."));
    }

    /// <summary>
    /// POST /api/v1/attendance/clock-out
    /// Clocks the current employee out and auto-calculates work hours (US-ATT-002 FR-1..FR-4/FR-7,
    /// AC-1..AC-5, BR-1..BR-4/BR-6). The source IP and user agent are captured here from the HTTP
    /// context and passed to the command (§7); clock_out is set server-side in UTC. Coordinates are
    /// conditional on the tenant geolocation policy (AC-5). Returns 404 when there is no open record.
    /// Gated by Attendance.CheckIn — the same self permission as clock-in.
    /// </summary>
    [HttpPost("clock-out")]
    [RequirePermission("Attendance.CheckIn")]
    [ProducesResponseType(typeof(ApiResponse<ClockOutResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClockOut(
        [FromBody] ClockOutRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.FirstOrDefault();

        var command = new ClockOutCommand(
            request.Latitude,
            request.Longitude,
            ipAddress,
            userAgent);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ClockOutResultDto>.Ok(result.Value!, "Clocked out."));
    }

    /// <summary>
    /// GET /api/v1/attendance/status
    /// Returns the current clock-in status for the authenticated employee in the current tenant:
    /// whether an open AttendanceLog exists (and when it started), the tenant's RequireGeolocation
    /// flag (BR-2, drives the UI's AC-3 vs AC-4 branch), and null shift fields (US-ATT-005 not built).
    /// Gated by Attendance.CheckIn — the same self permission as clock-in.
    /// </summary>
    [HttpGet("status")]
    [RequirePermission("Attendance.CheckIn")]
    [ProducesResponseType(typeof(ApiResponse<ClockStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetClockStatusQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ClockStatusDto>.Ok(result.Value!));
    }
}
