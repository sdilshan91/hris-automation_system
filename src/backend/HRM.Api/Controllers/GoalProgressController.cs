using HRM.Application.Common.Models;
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
/// Tenant-scoped endpoints for goal tracking with progress updates (US-PRF-009). Employees list their own goals
/// and post APPEND-ONLY progress updates against them (AC-1/AC-2) under <c>Performance.Read.Self</c>; managers /
/// HR view the team goal-progress summary, drill into an employee's goals, and comment on a goal thread
/// (AC-3/AC-4/FR-8) under <c>Performance.Review.Team</c> (direct reports) OR <c>Performance.Review.All</c> (HR) —
/// the service enforces the direct-report scope + the BR-5 visibility (employee / manager / HR only, not peers).
/// There is intentionally NO update/delete endpoint for a posted progress update — the history is immutable
/// (NFR-3/FR-3). All operations are tenant-scoped via the EF global query filter (NFR-2).
///
/// NOTIFICATIONS (FR-5): posting an update notifies the manager via the log-only seam; real-time/SignalR delivery
/// is a deferred hook tied to US-NTF-001 (not built — SignalR is not set up in this codebase).
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance")]
[Authorize]
public sealed class GoalProgressController : ControllerBase
{
    private readonly IMediator _mediator;

    public GoalProgressController(IMediator mediator) => _mediator = mediator;

    private IActionResult TimelineResult(Result<GoalTimelineDto> result)
    {
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<GoalTimelineDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/performance/my-goals — the caller's active goals with current progress (AC-1).</summary>
    [HttpGet("my-goals")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MyGoalProgressDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyGoals(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyGoalsQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<MyGoalProgressDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/goals/{goalId}/progress — post an append-only progress update (AC-2/FR-1/
    /// FR-2). Employee-only on their OWN goal; only during the active cycle window (BR-1). 100% auto-sets Completed
    /// (BR-2); Blocked notifies manager + HR (BR-3). Returns the goal's updated timeline.
    /// </summary>
    [HttpPost("goals/{goalId:guid}/progress")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<GoalTimelineDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddProgress(
        Guid goalId, [FromBody] AddGoalProgressRequest request, CancellationToken cancellationToken)
    {
        var input = new AddGoalProgressInput(
            goalId, request.ProgressPct, request.Status, request.Notes, request.Attachments ?? []);
        return TimelineResult(await _mediator.Send(new AddGoalProgressCommand(input), cancellationToken));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/goals/{goalId}/timeline — one goal's chronological update history + comment
    /// thread (AC-3/FR-3/FR-8). VISIBILITY-restricted: employee / manager / HR only (BR-5) — else 403.
    /// </summary>
    [HttpGet("goals/{goalId:guid}/timeline")]
    [RequirePermission("Performance.Read.Self", "Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<GoalTimelineDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeline(Guid goalId, CancellationToken cancellationToken)
        => TimelineResult(await _mediator.Send(new GetGoalTimelineQuery(goalId), cancellationToken));

    /// <summary>
    /// GET /api/v1/tenant/performance/team-goals — the manager team goal-progress summary (AC-4). Scoped to the
    /// caller's direct reports (Review.Team) or all in-tenant employees (Review.All). Weighted completion (FR-4).
    /// </summary>
    [HttpGet("team-goals")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TeamGoalProgressRowDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTeamSummary(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTeamGoalSummaryQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<TeamGoalProgressRowDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/team-goals/employees/{employeeId} — drill-down to one in-scope employee's
    /// goals with current progress (AC-4). Scope-restricted to a direct report (Review.Team) / any (Review.All).
    /// </summary>
    [HttpGet("team-goals/employees/{employeeId:guid}")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MyGoalProgressDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEmployeeGoals(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeGoalProgressQuery(employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<MyGoalProgressDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/goals/{goalId}/comments — add a comment to a goal's thread, optionally
    /// anchored to a specific update (FR-8). The thread is TWO-WAY (ISSUE-141): the goal OWNER may reply on their
    /// own goal (<c>Performance.Read.Self</c>), and the owner's manager (<c>Performance.Review.Team</c>) or HR
    /// (<c>Performance.Review.All</c>) may comment on it. The service scopes a Read.Self caller to goals they own —
    /// they cannot comment on anyone else's goal. Returns the goal's updated timeline.
    /// </summary>
    [HttpPost("goals/{goalId:guid}/comments")]
    [RequirePermission("Performance.Read.Self", "Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<GoalTimelineDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddComment(
        Guid goalId, [FromBody] AddGoalCommentRequest request, CancellationToken cancellationToken)
    {
        var input = new AddGoalCommentInput(goalId, request.ProgressUpdateId, request.Body);
        return TimelineResult(await _mediator.Send(new AddGoalCommentCommand(input), cancellationToken));
    }
}
