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
/// Tenant-scoped manager/HR endpoints for performance reviews (US-PRF-003). Every action requires
/// <c>Performance.Review.Team</c> (direct manager) or <c>Performance.Review.All</c> (HR override). The
/// service then enforces BR-2 (the caller must be the employee's direct manager unless they hold the All
/// scope), the BR-1/AC-5 manager-review-window gate, the AC-3/FR-3 submit rules, the BR-4 final-score blend,
/// and the AC-5/BR-3 reopen (HR-only). All operations are tenant-scoped via the EF global query filter (NFR-2).
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance")]
[Authorize]
public sealed class ManagerReviewController : ControllerBase
{
    private readonly IMediator _mediator;

    public ManagerReviewController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}
    /// The manager's review workspace for an employee + cycle: each goal with the employee's self-rating/
    /// comment alongside the manager's rating/comment, plus scale/weights and open/locked flags (AC-1).
    /// </summary>
    [HttpGet("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ManagerReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkspace(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetManagerReviewWorkspaceQuery(employeeId, cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ManagerReviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/reviews/cycles/{cycleId}/team
    /// Team-reviews dashboard for the calling manager + cycle: each direct report with their review status
    /// (pending self-assessment / self-assessment submitted / manager review pending / completed) (AC-4).
    /// </summary>
    [HttpGet("reviews/cycles/{cycleId:guid}/team")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<TeamReviewsDashboardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeam(Guid cycleId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTeamReviewsDashboardQuery(cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<TeamReviewsDashboardDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/performance/reviews/draft
    /// Saves partial manager-review progress without submitting.
    /// </summary>
    [HttpPut("reviews/draft")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ManagerReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SaveDraft(
        [FromBody] SaveManagerReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SaveManagerReviewDraftCommand(
                request.CycleId, request.EmployeeId, request.SummaryComment, request.Flag, request.Items),
            cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ManagerReviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/reviews/submit
    /// Submits the manager review — requires all goals rated with comments ≥20 chars (returns the unrated
    /// goal list on failure); computes the weighted manager score + final combined score, locks the record,
    /// and notifies the employee (AC-2/AC-3/BR-4).
    /// </summary>
    [HttpPost("reviews/submit")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ManagerReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(
        [FromBody] SaveManagerReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SubmitManagerReviewCommand(
                request.CycleId, request.EmployeeId, request.SummaryComment, request.Flag, request.Items),
            cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ManagerReviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/reopen
    /// Reopens a submitted review for editing — HR-only (Performance.Review.All), AC-5/BR-3.
    /// </summary>
    [HttpPost("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}/reopen")]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ManagerReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ReopenManagerReviewCommand(employeeId, cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ManagerReviewDto>.Ok(result.Value!));
    }
}
