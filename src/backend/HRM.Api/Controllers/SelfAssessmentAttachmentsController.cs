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
/// Self-assessment evidence-file attachments (US-PRF-002 FR-5 / ISSUE-105). Every action requires
/// <c>Performance.Read.Self</c> (same gate as the ratings endpoints in <see cref="SelfAssessmentController"/>);
/// the service then scopes every read/write to the CALLING employee's own self-assessment, so an employee can
/// only ever attach/list/download their own evidence (NFR-2). Uploads are virus-scanned before storage
/// (NFR-4). All operations are tenant-scoped via the EF global query filter. Kept as a SEPARATE controller
/// (mirroring how recruitment splits <c>ApplicantsController</c> / <c>ApplicantPipelineController</c>).
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance/self-assessments")]
[Authorize]
public sealed class SelfAssessmentAttachmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SelfAssessmentAttachmentsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// POST /api/v1/tenant/performance/self-assessments/cycles/{cycleId}/goals/{goalId}/attachments
    /// (multipart/form-data) — uploads a virus-scanned evidence file for one goal of the caller's own
    /// self-assessment (FR-5). Max 5 files per goal, ≤10 MB each, PDF/image/Office types only.
    /// </summary>
    [HttpPost("cycles/{cycleId:guid}/goals/{goalId:guid}/attachments")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<SelfAssessmentAttachmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload(
        Guid cycleId, Guid goalId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("A file is required.", "file_required"));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(
            new UploadSelfAssessmentAttachmentCommand(
                cycleId, goalId, stream, file.FileName, file.ContentType, file.Length),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<SelfAssessmentAttachmentDto>.Ok(result.Value!, "Evidence file uploaded."));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/self-assessments/cycles/{cycleId}/goals/{goalId}/attachments
    /// Lists the evidence-file metadata (no bytes) attached to one goal of the caller's own self-assessment.
    /// </summary>
    [HttpGet("cycles/{cycleId:guid}/goals/{goalId:guid}/attachments")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SelfAssessmentAttachmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid cycleId, Guid goalId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ListSelfAssessmentAttachmentsQuery(cycleId, goalId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<SelfAssessmentAttachmentDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/self-assessments/attachments/{attachmentId}
    /// Streams the evidence file through the API with a Content-Disposition filename (no direct blob URL).
    /// Tenant-scoped + ownership-checked; 404 if it does not belong to the caller's own self-assessment.
    /// </summary>
    [HttpGet("attachments/{attachmentId:guid}")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid attachmentId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DownloadSelfAssessmentAttachmentQuery(attachmentId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
