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
/// Tenant-scoped employee self-assessment endpoints (US-PRF-002). Every action requires
/// <c>Performance.Read.Self</c>; the service then scopes every read/write to the CALLING employee's own
/// record, so an employee can only ever see/edit their own self-assessment (NFR-2). All operations are
/// tenant-scoped via the EF global query filter. The self-assessment-window gate (BR-1/AC-4), all-goals-
/// rated + comment-length submit rules (BR-2/FR-3), weighted-score calc (FR-4) and submitted-lock (BR-3)
/// are enforced in <c>SelfAssessmentService</c>.
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance/self-assessments")]
[Authorize]
public sealed class SelfAssessmentController : ControllerBase
{
    private readonly IMediator _mediator;

    public SelfAssessmentController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/tenant/performance/self-assessments/cycles/{cycleId}/me
    /// Returns the calling employee's self-assessment for the cycle: all assigned goals + any saved
    /// ratings, the rating-scale/self-weight config, and the open/locked flags (AC-1).
    /// </summary>
    [HttpGet("cycles/{cycleId:guid}/me")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<SelfAssessmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMine(Guid cycleId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMySelfAssessmentQuery(cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<SelfAssessmentDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/performance/self-assessments/draft
    /// Saves partial self-assessment progress without submitting (AC-3/FR-6).
    /// </summary>
    [HttpPut("draft")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<SelfAssessmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SaveDraft(
        [FromBody] SaveSelfAssessmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SaveSelfAssessmentDraftCommand(request.CycleId, request.Items), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<SelfAssessmentDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/self-assessments/submit
    /// Submits the self-assessment — requires all goals rated with comments ≥20 chars; computes the
    /// weighted self-score, locks the record, and notifies the manager (AC-2/BR-2/BR-3/FR-3/FR-4).
    /// </summary>
    [HttpPost("submit")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<SelfAssessmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(
        [FromBody] SaveSelfAssessmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SubmitSelfAssessmentCommand(request.CycleId, request.Items), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<SelfAssessmentDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/self-assessments/cycles/{cycleId}/employees/{employeeId}/reopen
    /// Reopens a submitted self-assessment back to Draft so the employee can edit and re-submit
    /// (US-PRF-004 BR-3/AC-2 — BUG-059). Manager/HR-only: requires Performance.Review.Team (direct manager)
    /// or Performance.Review.All (HR); the service enforces the manager-of-record check and rejects if the
    /// cycle's self-assessment window has closed. An employee can never self-reopen.
    /// </summary>
    [HttpPost("cycles/{cycleId:guid}/employees/{employeeId:guid}/reopen")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<SelfAssessmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen(
        Guid cycleId, Guid employeeId, [FromBody] ReopenSelfAssessmentRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ReopenSelfAssessmentCommand(employeeId, cycleId, request?.Reason), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<SelfAssessmentDto>.Ok(result.Value!));
    }
}
