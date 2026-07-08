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
/// Tenant-scoped 360-degree feedback endpoints (US-PRF-005). Reviewer CONFIGURATION + RESULTS are HR-only
/// (<c>Performance.Review.All</c>); reviewers SUBMIT feedback for their own assignments (the service resolves
/// the caller's employee record and checks the Pending assignment — any authenticated user may call submit,
/// authorization is enforced in the service so reviewers across all four categories can act on their own
/// assignment). Anonymity (BR-5/NFR-3/FR-5) is enforced in the service projection. All operations are
/// tenant-scoped via the EF global query filter (NFR-2).
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance")]
[Authorize]
public sealed class Feedback360Controller : ControllerBase
{
    private readonly IMediator _mediator;

    public Feedback360Controller(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/tenant/performance/360/cycles/{cycleId}/employees/{employeeId}/reviewers
    /// Reviewer-configuration view for an employee + cycle (AC-1): persisted assignments + auto-suggested
    /// peers/direct-reports. HR-only.
    /// </summary>
    [HttpGet("360/cycles/{cycleId:guid}/employees/{employeeId:guid}/reviewers")]
    // BUG-244: HR (Review.All) OR a Team manager (Review.Team) pass the coarse gate; the service enforces the
    // fine tenant-toggle + direct-report check (AuthorizeConfigureAsync).
    [RequirePermission("Performance.Review.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<ReviewerConfigurationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewers(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetReviewerConfigurationQuery(cycleId, employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ReviewerConfigurationDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/360/cycles/{cycleId}/employees/{employeeId}/reviewers
    /// Manually adds a reviewer in a category (AC-1/FR-2). Enforces BR-2 (no self-as-peer) + BR-3 de-dup. HR-only.
    /// </summary>
    [HttpPost("360/cycles/{cycleId:guid}/employees/{employeeId:guid}/reviewers")]
    // BUG-244: HR OR Team manager (in-service fine check — AuthorizeConfigureAsync).
    [RequirePermission("Performance.Review.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<ReviewerAssignmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddReviewer(
        Guid cycleId, Guid employeeId, [FromBody] AddReviewerRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddReviewerCommand(cycleId, employeeId, request.ReviewerEmployeeId, request.Category), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ReviewerAssignmentDto>.Ok(result.Value!));
    }

    /// <summary>
    /// DELETE /api/v1/tenant/performance/360/reviewers/{assignmentId}
    /// Removes a reviewer assignment (AC-1/FR-2). Cannot remove one that has already submitted. HR-only.
    /// </summary>
    [HttpDelete("360/reviewers/{assignmentId:guid}")]
    // BUG-244: HR OR Team manager (in-service fine check — AuthorizeConfigureAsync).
    [RequirePermission("Performance.Review.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveReviewer(Guid assignmentId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RemoveReviewerCommand(assignmentId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse.Ok());
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/feedback-360/employees/{employeeId}/config
    /// BUG-244: the config-screen LOAD (no cycleId). Resolves the active 360-enabled cycle server-side and
    /// returns the full reviewer-configuration view WITH the Self/Manager auto-seed (same payload as the
    /// cycle-keyed config GET). HR OR a Team manager of the reviewee (tenant-configurable) — the service
    /// enforces the fine tenant-toggle + direct-report check.
    /// </summary>
    [HttpGet("feedback-360/employees/{employeeId:guid}/config")]
    [RequirePermission("Performance.Review.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<ReviewerConfigurationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetActiveConfig(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetActiveReviewerConfigurationQuery(employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ReviewerConfigurationDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/performance/feedback-360/employees/{employeeId}/reviewers
    /// BUG-244 #1: full-replace of an employee's manual (Peer + Direct Report) 360 reviewers in the active
    /// 360-enabled cycle. Self + Manager are server-owned and untouched. HR OR a Team manager of the reviewee
    /// (tenant-configurable) — the service enforces the fine tenant-toggle + direct-report check. Returns the
    /// refreshed configuration.
    /// </summary>
    [HttpPut("feedback-360/employees/{employeeId:guid}/reviewers")]
    [RequirePermission("Performance.Review.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<ReviewerConfigurationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SaveReviewers(
        Guid employeeId, [FromBody] SaveReviewersRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SaveReviewersCommand(employeeId, request.Reviewers ?? []), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ReviewerConfigurationDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/feedback-360/employees/{employeeId}/tracker
    /// BUG-244 #3: per-category submitted/pending/overdue completion tracker for an employee in the active
    /// 360-enabled cycle. Same authorization as the reviewer config (HR OR direct manager). Overdue is always 0
    /// (no 360 window entity); only the Peer category carries a configured minimum.
    /// </summary>
    [HttpGet("feedback-360/employees/{employeeId:guid}/tracker")]
    [RequirePermission("Performance.Review.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<CompletionTrackerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTracker(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTrackerQuery(employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CompletionTrackerDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/feedback-360/assignments/{assignmentId}/form
    /// BUG-244 #2: ONE reviewer's feedback form for a single assignment (FR-4 / AC-2 deep link). The cycle comes
    /// from the assignment. RLS is enforced in the service (the caller's employee must be the assigned reviewer,
    /// else 403 not_assigned) — no [RequirePermission], mirroring SubmitFeedback.
    /// </summary>
    [HttpGet("feedback-360/assignments/{assignmentId:guid}/form")]
    [ProducesResponseType(typeof(ApiResponse<FeedbackFormDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFeedbackForm(Guid assignmentId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetFeedbackFormQuery(assignmentId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<FeedbackFormDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/feedback-360/assignments/{assignmentId}/submit
    /// BUG-244: submit the reviewer form keyed by the assignment id (AC-3). The cycle + reviewee + reviewer are
    /// resolved from the assignment; RLS is enforced in the service (caller's employee must be the assigned
    /// reviewer, else 403 not_assigned) — no [RequirePermission], mirroring the form load + the cycle-keyed
    /// submit. Each answer's questionId maps back to its goal rating. Returns the now-locked form.
    /// </summary>
    [HttpPost("feedback-360/assignments/{assignmentId:guid}/submit")]
    [ProducesResponseType(typeof(ApiResponse<FeedbackFormDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SubmitFeedbackByAssignment(
        Guid assignmentId, [FromBody] SubmitFeedbackByAssignmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SubmitFeedbackByAssignmentCommand(assignmentId, request.Answers ?? []), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<FeedbackFormDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/360/cycles/{cycleId}/employees/{employeeId}/notify
    /// Notifies all assigned reviewers that the 360 phase has started (AC-2). HR-only.
    /// </summary>
    [HttpPost("360/cycles/{cycleId:guid}/employees/{employeeId:guid}/notify")]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> NotifyReviewers(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new NotifyReviewersCommand(cycleId, employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<int>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/360/cycles/{cycleId}/employees/{employeeId}/feedback
    /// Submits the calling reviewer's 360 feedback (AC-3). The reviewer is resolved from the caller; they must
    /// hold a Pending assignment for this employee (any authenticated user may reach the endpoint, the service
    /// authorizes against the assignment so all four categories — self/manager/peer/report — can self-submit).
    /// </summary>
    [HttpPost("360/cycles/{cycleId:guid}/employees/{employeeId:guid}/feedback")]
    [ProducesResponseType(typeof(ApiResponse<Feedback360ResultEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SubmitFeedback(
        Guid cycleId, Guid employeeId, [FromBody] SubmitFeedback360Request request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SubmitFeedback360Command(cycleId, employeeId, request.OverallComment, request.Items), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<Feedback360ResultEntryDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/360/cycles/{cycleId}/employees/{employeeId}/results
    /// Aggregated 360 results (AC-4/FR-6): composite score, per-competency + per-category averages,
    /// completion tracker, anonymized individual entries, BR-4 peer-threshold warning. HR-only.
    /// </summary>
    [HttpGet("360/cycles/{cycleId:guid}/employees/{employeeId:guid}/results")]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<Feedback360ResultsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResults(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetFeedback360ResultsQuery(cycleId, employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<Feedback360ResultsDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/360/cycles/{cycleId}/employees/{employeeId}/report
    /// 360 summary report data for PDF export (FR-7). Same payload as /results; the dedicated route signals
    /// export intent. PDF RENDERING IS A SEAM — see the QuestPDF-backed renderer note in the PR. HR-only.
    /// </summary>
    [HttpGet("360/cycles/{cycleId:guid}/employees/{employeeId:guid}/report")]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<Feedback360ResultsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReport(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetFeedback360ReportQuery(cycleId, employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<Feedback360ResultsDto>.Ok(result.Value!));
    }
}
