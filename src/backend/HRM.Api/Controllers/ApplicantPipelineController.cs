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
/// Recruiter-facing applicant pipeline (Kanban) endpoints for US-REC-003. All operations are
/// authenticated and tenant-scoped via the EF global query filter (AC-5). Reads require
/// <c>Recruitment.View</c> (BR-1); stage moves and bulk moves require <c>Recruitment.Manage</c>
/// (BR-2/BR-4). The vacancy-scoped applicant list/detail submit paths live in
/// <see cref="ApplicantsController"/>; this controller adds the board view, the rich detail (with stage
/// timeline), and the stage-management actions.
/// </summary>
[ApiController]
[Route("api/v1/recruitment")]
[Authorize]
public sealed class ApplicantPipelineController : ControllerBase
{
    private readonly IMediator _mediator;

    public ApplicantPipelineController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/recruitment/vacancies/{vacancyId}/pipeline
    /// Returns the Kanban board for a vacancy: one column per stage with per-stage counts + total
    /// (AC-1/FR-1/FR-5), after applying the optional filters (stage / source / appliedFrom / appliedTo /
    /// search) — FR-6/AC-4. Requires Recruitment.View (BR-1).
    /// </summary>
    [HttpGet("vacancies/{vacancyId:guid}/pipeline")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<ApplicantPipelineBoardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPipeline(
        Guid vacancyId,
        [FromQuery] ApplicantStage? stage,
        [FromQuery] ApplicationSource? source,
        [FromQuery] DateTime? appliedFrom,
        [FromQuery] DateTime? appliedTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var filter = new PipelineFilter
        {
            Stage = stage,
            Source = source,
            AppliedFrom = appliedFrom,
            AppliedTo = appliedTo,
            Search = search,
        };

        var result = await _mediator.Send(new GetApplicantPipelineQuery(vacancyId, filter), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ApplicantPipelineBoardDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/applicants/{applicantId}/detail
    /// Returns the full applicant detail for the slide-over panel (AC-3/FR-7): profile + stage-transition
    /// timeline + resume download reference. Interview data is deferred (US-REC-005/006). Requires
    /// Recruitment.View (BR-1).
    /// </summary>
    [HttpGet("applicants/{applicantId:guid}/detail")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<ApplicantDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid applicantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetApplicantDetailQuery(applicantId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ApplicantDetailDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/applicants/{applicantId}/resume
    /// Streams the applicant's resume through the API with a Content-Disposition filename (FR-7/NFR-5),
    /// rather than exposing a direct blob-storage URL. Requires Recruitment.View (BR-1).
    /// </summary>
    [HttpGet("applicants/{applicantId:guid}/resume")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadResume(Guid applicantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetApplicantResumeQuery(applicantId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var resume = result.Value!;
        return File(resume.Content, resume.ContentType, resume.FileName);
    }

    /// <summary>
    /// POST /api/v1/recruitment/applicants/{applicantId}/move-stage
    /// Moves a single applicant to a new pipeline stage (AC-2/FR-3). Writes a stage-history row +
    /// audit-log entry. Rejected requires a reason (BR-3); a backward move requires a reason (BR-4).
    /// Requires Recruitment.Manage (BR-2).
    /// </summary>
    [HttpPost("applicants/{applicantId:guid}/move-stage")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<MoveApplicantStageResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MoveStage(
        Guid applicantId,
        [FromBody] MoveApplicantStageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new MoveApplicantStageCommand(applicantId, request.ToStage, request.Reason, request.Notes, request.RejectionReason, request.RowVersion),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<MoveApplicantStageResultDto>.Ok(result.Value!, "Applicant stage updated."));
    }

    /// <summary>
    /// POST /api/v1/recruitment/applicants/bulk-move-stage
    /// Moves several applicants to the same stage in one action (FR-8). All-or-nothing. Same reason rules
    /// as the single move (BR-3/BR-4). Requires Recruitment.Manage (BR-2). CSV export and bulk email
    /// (also FR-8) are deferred.
    /// </summary>
    [HttpPost("applicants/bulk-move-stage")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<BulkMoveApplicantStageResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BulkMoveStage(
        [FromBody] BulkMoveApplicantStageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new BulkMoveApplicantStageCommand(request.ApplicantIds, request.ToStage, request.Reason, request.Notes, request.RejectionReason),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<BulkMoveApplicantStageResultDto>.Ok(
            result.Value!, $"{result.Value!.MovedCount} applicant(s) moved."));
    }
}
