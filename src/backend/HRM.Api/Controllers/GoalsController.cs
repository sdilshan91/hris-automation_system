using HRM.Application.DTOs;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;
using HRM.Application.Features.Performance.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped manager/HR endpoints for performance goal-setting (US-PRF-001). Writes require
/// <c>Performance.SetGoal.Team</c> (direct manager) or <c>Performance.SetGoal.All</c> (HR override) — the
/// service then enforces BR-4 (the caller must be the employee's direct manager unless they hold the All
/// scope) and the BR-1/AC-5 goal-setting-window gate. All operations are tenant-scoped via the EF global
/// query filter (NFR-2). The [RequirePermission] attribute admits either scope; the finer-grained
/// direct-report check is in <c>GoalService</c>.
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance")]
[Authorize]
public sealed class GoalsController : ControllerBase
{
    private readonly IMediator _mediator;

    public GoalsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/tenant/performance/employees/{employeeId}/cycles/{cycleId}/goals
    /// Returns an employee's goals for a cycle + the rolled-up weight total (AC-2/AC-3).
    /// </summary>
    [HttpGet("employees/{employeeId:guid}/cycles/{cycleId:guid}/goals")]
    [RequirePermission("Performance.SetGoal.Team", "Performance.SetGoal.All")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeGoalsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployeeGoals(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeGoalsQuery(employeeId, cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<EmployeeGoalsDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/cycles/{cycleId}/team-dashboard
    /// Team goals dashboard for the calling manager + cycle (AC-4).
    /// </summary>
    [HttpGet("cycles/{cycleId:guid}/team-dashboard")]
    [RequirePermission("Performance.SetGoal.Team", "Performance.SetGoal.All")]
    [ProducesResponseType(typeof(ApiResponse<TeamGoalsDashboardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamDashboard(Guid cycleId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTeamGoalsDashboardQuery(cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<TeamGoalsDashboardDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/goals/{id}
    /// Resolves a single goal via the employee/cycle scope (used by CreatedAtAction).
    /// </summary>
    [HttpGet("goals/{id:guid}", Name = "GetGoalById")]
    [RequirePermission("Performance.SetGoal.Team", "Performance.SetGoal.All")]
    [ProducesResponseType(typeof(ApiResponse<GoalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetGoalById(Guid id)
        // Single-goal read is served via the employee-goals endpoint; this named route exists only so
        // CreatedAtAction can produce a Location header. Clients fetch the full set via GetEmployeeGoals.
        => Ok(ApiResponse.Ok());

    /// <summary>
    /// POST /api/v1/tenant/performance/goals
    /// Creates a goal for a direct report (FR-1/AC-2).
    /// </summary>
    [HttpPost("goals")]
    [RequirePermission("Performance.SetGoal.Team", "Performance.SetGoal.All")]
    [ProducesResponseType(typeof(ApiResponse<GoalDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateGoalRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateGoalCommand(
            request.CycleId, request.EmployeeId, request.Title, request.Description, request.Category,
            request.Weight, request.TargetValue, request.MeasurementUnit, request.DueDate, request.ParentGoalId);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtRoute("GetGoalById", new { id = result.Value!.Id }, ApiResponse<GoalDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/performance/goals/{id}
    /// Updates an existing goal (FR-1).
    /// </summary>
    [HttpPut("goals/{id:guid}")]
    [RequirePermission("Performance.SetGoal.Team", "Performance.SetGoal.All")]
    [ProducesResponseType(typeof(ApiResponse<GoalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateGoalRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateGoalCommand(
            id, request.Title, request.Description, request.Category, request.Weight,
            request.TargetValue, request.MeasurementUnit, request.DueDate, request.ParentGoalId,
            request.RowVersion);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<GoalDto>.Ok(result.Value!));
    }

    /// <summary>
    /// DELETE /api/v1/tenant/performance/goals/{id}
    /// Soft-deletes a goal (FR-1).
    /// </summary>
    [HttpDelete("goals/{id:guid}")]
    [RequirePermission("Performance.SetGoal.Team", "Performance.SetGoal.All")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteGoalCommand(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse.Ok());
    }
}
