using HRM.Application.DTOs;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Enums;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Recruiter-facing interview scheduling endpoints (US-REC-005). All operations are authenticated and
/// tenant-scoped via the EF global query filter (AC-5). Writes (schedule / reschedule / cancel) require
/// <c>Recruitment.Manage</c> (BR-1); calendar + detail reads require <c>Recruitment.View</c>.
/// On a successful schedule/reschedule the response may carry soft scheduling-conflict warnings
/// (FR-7) on <c>data.warnings</c> — the interview is still saved (override allowed).
/// </summary>
[ApiController]
[Route("api/v1/recruitment")]
[Authorize]
public sealed class InterviewsController : ControllerBase
{
    private readonly IMediator _mediator;

    public InterviewsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// POST /api/v1/recruitment/interviews
    /// Schedules a new interview for an applicant (AC-1/FR-1/FR-2). Requires Recruitment.Manage (BR-1).
    /// </summary>
    [HttpPost("interviews")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<InterviewDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Schedule(
        [FromBody] ScheduleInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ScheduleInterviewCommand(
            request.ApplicantId, request.InterviewType, request.ScheduledDate, request.StartTime,
            request.DurationMinutes, request.Location, request.VideoLink, request.Notes,
            request.InterviewerEmployeeIds), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<InterviewDto>.Ok(result.Value!, "Interview scheduled."));
    }

    /// <summary>
    /// PUT /api/v1/recruitment/interviews/{interviewId}
    /// Reschedules / updates an interview (AC-3/BR-6). Requires Recruitment.Manage (BR-1).
    /// </summary>
    [HttpPut("interviews/{interviewId:guid}")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<InterviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid interviewId,
        [FromBody] UpdateInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateInterviewCommand(
            interviewId, request.InterviewType, request.ScheduledDate, request.StartTime,
            request.DurationMinutes, request.Location, request.VideoLink, request.Notes,
            request.InterviewerEmployeeIds), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<InterviewDto>.Ok(result.Value!, "Interview rescheduled."));
    }

    /// <summary>
    /// POST /api/v1/recruitment/interviews/{interviewId}/cancel
    /// Cancels an interview (AC-3). Requires Recruitment.Manage (BR-1).
    /// </summary>
    [HttpPost("interviews/{interviewId:guid}/cancel")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<InterviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid interviewId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelInterviewCommand(interviewId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<InterviewDto>.Ok(result.Value!, "Interview cancelled."));
    }

    /// <summary>
    /// GET /api/v1/recruitment/interviews/{interviewId}
    /// Gets a single interview (tenant-scoped). Requires Recruitment.View.
    /// </summary>
    [HttpGet("interviews/{interviewId:guid}")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<InterviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid interviewId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetInterviewByIdQuery(interviewId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<InterviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/interviews?interviewerEmployeeId=&amp;vacancyId=&amp;from=&amp;to=&amp;status=
    /// Calendar view (FR-5): lists tenant interviews, filterable by interviewer / vacancy / date-range /
    /// status. Requires Recruitment.View.
    /// </summary>
    [HttpGet("interviews")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<InterviewDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? interviewerEmployeeId,
        [FromQuery] Guid? vacancyId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] InterviewStatus? status,
        CancellationToken cancellationToken)
    {
        var filter = new InterviewCalendarFilter
        {
            InterviewerEmployeeId = interviewerEmployeeId,
            VacancyId = vacancyId,
            From = from,
            To = to,
            Status = status,
        };

        var result = await _mediator.Send(new ListInterviewsQuery(filter), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<InterviewDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/applicants/{applicantId}/interviews
    /// Lists all interviews for an applicant, newest round first (FR-2). Requires Recruitment.View.
    /// </summary>
    [HttpGet("applicants/{applicantId:guid}/interviews")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<InterviewDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByApplicant(Guid applicantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListInterviewsByApplicantQuery(applicantId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<InterviewDto>>.Ok(result.Value!));
    }

    // ── Scorecards (US-REC-006) ───────────────────────────────────────

    /// <summary>
    /// POST /api/v1/recruitment/interviews/{interviewId}/scorecard
    /// Submits (or edits) the current interviewer's structured scorecard (AC-1/FR-1..FR-7). Guarded by
    /// Recruitment.View; the service enforces BR-1 (only the assigned interviewer — 403 otherwise) and BR-4
    /// (the lock period — 409 once locked). On the final interviewer's submission the interview is marked
    /// Completed (FR-4).
    /// </summary>
    [HttpPost("interviews/{interviewId:guid}/scorecard")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<ScorecardDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitScorecard(
        Guid interviewId,
        [FromBody] SubmitScorecardRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SubmitScorecardCommand(
            interviewId, request.OverallRecommendation, request.GeneralNotes,
            (request.Ratings ?? []).Select(r => new CriterionRatingInput
            {
                CriterionKey = r.CriterionKey,
                Score = r.Score,
                Comment = r.Comment,
            }).ToList()), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<ScorecardDto>.Ok(result.Value!, "Scorecard submitted."));
    }

    /// <summary>
    /// GET /api/v1/recruitment/interviews/{interviewId}/scorecards
    /// Lists the scorecards for an interview the caller may see + the aggregate average (AC-2/AC-3).
    /// Guarded by Recruitment.View; the service applies the anti-bias rule (FR-6/BR-5).
    /// </summary>
    [HttpGet("interviews/{interviewId:guid}/scorecards")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<ScorecardSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScorecardsForInterview(Guid interviewId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetScorecardsForInterviewQuery(interviewId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ScorecardSummaryDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/applicants/{applicantId}/scorecards
    /// Lists the scorecards across all of an applicant's interviews the caller may see + the aggregate
    /// average (AC-2/AC-3). Guarded by Recruitment.View; the service applies the anti-bias rule (FR-6/BR-5).
    /// </summary>
    [HttpGet("applicants/{applicantId:guid}/scorecards")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<ScorecardSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScorecardsForApplicant(Guid applicantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetScorecardsForApplicantQuery(applicantId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ScorecardSummaryDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/scorecard-criteria
    /// Returns the default evaluation criteria the scorecard form renders (FR-1/BR-2). Guarded by
    /// Recruitment.View.
    /// </summary>
    [HttpGet("scorecard-criteria")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ScorecardCriterionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScorecardCriteria(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetScorecardCriteriaQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<ScorecardCriterionDto>>.Ok(result.Value!));
    }
}

/// <summary>
/// Request body to schedule an interview (US-REC-005 FR-1). camelCase on the wire. The round number is
/// server-assigned (FR-2), so it is not part of the request.
/// </summary>
public sealed record ScheduleInterviewRequest
{
    public Guid ApplicantId { get; init; }
    public InterviewType InterviewType { get; init; }
    public DateOnly ScheduledDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public int DurationMinutes { get; init; } = 60;
    public string? Location { get; init; }
    public string? VideoLink { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<Guid> InterviewerEmployeeIds { get; init; } = [];
}

/// <summary>Request body to reschedule/update an interview (US-REC-005 AC-3/BR-6). camelCase on the wire.</summary>
public sealed record UpdateInterviewRequest
{
    public InterviewType InterviewType { get; init; }
    public DateOnly ScheduledDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public int DurationMinutes { get; init; } = 60;
    public string? Location { get; init; }
    public string? VideoLink { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<Guid> InterviewerEmployeeIds { get; init; } = [];
}

/// <summary>
/// Request body to submit/edit a scorecard (US-REC-006 FR-1). camelCase on the wire. The interview id comes
/// from the route and the interviewer from the auth context (BR-1), so neither is in the body.
/// </summary>
public sealed record SubmitScorecardRequest
{
    public OverallRecommendation OverallRecommendation { get; init; }
    public string? GeneralNotes { get; init; }
    public IReadOnlyList<CriterionRatingRequest> Ratings { get; init; } = [];
}

/// <summary>A single criterion rating in a scorecard submission (US-REC-006 FR-1). camelCase on the wire.</summary>
public sealed record CriterionRatingRequest
{
    public string CriterionKey { get; init; } = string.Empty;
    public int Score { get; init; }
    public string? Comment { get; init; }
}
