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
/// Tenant-scoped endpoints for performance-review meeting notes &amp; digital sign-off (US-PRF-006). The
/// manager-side actions (notes, request sign-off, export) require <c>Performance.Review.Team</c> (direct
/// manager) or <c>Performance.Review.All</c> (HR); the employee-side actions (acknowledge / dispute) require
/// <c>Performance.Read.Self</c> and the service enforces the caller IS the reviewed employee. HR dispute
/// resolution requires <c>Performance.Review.All</c>. The client IP is captured here (FR-7) and persisted on
/// each immutable sign-off. All operations are tenant-scoped via the EF global query filter (NFR-2).
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance")]
[Authorize]
public sealed class ReviewSignoffController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReviewSignoffController(IMediator mediator) => _mediator = mediator;

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult NotesResult(HRM.Application.Common.Models.Result<ReviewMeetingNotesDto> result)
    {
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ReviewMeetingNotesDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/notes
    /// The meeting notes + sign-off state. When none exist yet, returns the pre-populated template (AC-1).
    /// </summary>
    [HttpGet("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}/notes")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ReviewMeetingNotesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotes(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
        => NotesResult(await _mediator.Send(new GetMeetingNotesQuery(employeeId, cycleId), cancellationToken));

    /// <summary>
    /// PUT /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/notes
    /// Saves/updates the meeting notes WITHOUT requesting sign-off (AC-1). Only after the manager review is
    /// submitted (BR-1); rich text is sanitized server-side (FR-1).
    /// </summary>
    [HttpPut("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}/notes")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ReviewMeetingNotesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveNotes(
        Guid cycleId, Guid employeeId, [FromBody] SaveMeetingNotesRequest request, CancellationToken cancellationToken)
    {
        var input = new SaveMeetingNotesInput(
            cycleId, employeeId, request.Body, request.Strengths, request.DevelopmentAreas, request.Summary,
            request.Actions ?? []);
        return NotesResult(await _mediator.Send(new SaveMeetingNotesCommand(input), cancellationToken));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/request-signoff
    /// Saves notes and requests employee sign-off (AC-2): status → Pending Employee Sign-Off, notifies the
    /// employee, records an immutable manager sign-off entry (with client IP), starts the auto-close clock.
    /// </summary>
    [HttpPost("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}/request-signoff")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ReviewMeetingNotesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestSignOff(
        Guid cycleId, Guid employeeId, [FromBody] SaveMeetingNotesRequest request, CancellationToken cancellationToken)
    {
        var input = new SaveMeetingNotesInput(
            cycleId, employeeId, request.Body, request.Strengths, request.DevelopmentAreas, request.Summary,
            request.Actions ?? []);
        return NotesResult(await _mediator.Send(new RequestSignOffCommand(input, ClientIp), cancellationToken));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/acknowledge
    /// Employee acknowledges &amp; signs (AC-3/FR-4): records an immutable signature (name + timestamp + IP),
    /// finalizes status to Signed Off and LOCKS the review (BR-5). Caller must be the reviewed employee.
    /// </summary>
    [HttpPost("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}/acknowledge")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<ReviewMeetingNotesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Acknowledge(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
    {
        var input = new SignoffActionInput(cycleId, employeeId, Comments: null, ClientIpAddress: ClientIp);
        return NotesResult(await _mediator.Send(new AcknowledgeReviewCommand(input), cancellationToken));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/dispute
    /// Employee disputes (AC-3/FR-4/FR-5): mandatory comments; status → Disputed; notifies manager + HR.
    /// Records an immutable dispute signature (name + timestamp + IP). Caller must be the reviewed employee.
    /// </summary>
    [HttpPost("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}/dispute")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<ReviewMeetingNotesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Dispute(
        Guid cycleId, Guid employeeId, [FromBody] DisputeReviewRequest request, CancellationToken cancellationToken)
    {
        var input = new SignoffActionInput(cycleId, employeeId, request.Comments, ClientIp);
        return NotesResult(await _mediator.Send(new DisputeReviewCommand(input), cancellationToken));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/resolve-dispute
    /// HR resolves a disputed review (BR-4): confirm as-is (→ Signed Off + lock) or amend (→ Notes Added so
    /// the manager can revise and re-request). HR-only. Records an immutable HR sign-off entry.
    /// </summary>
    [HttpPost("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}/resolve-dispute")]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ReviewMeetingNotesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveDispute(
        Guid cycleId, Guid employeeId, [FromBody] ResolveDisputeRequest request, CancellationToken cancellationToken)
    {
        var input = new ResolveDisputeInput(cycleId, employeeId, request.Amend, request.Comments, ClientIp);
        return NotesResult(await _mediator.Send(new ResolveDisputeCommand(input), cancellationToken));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/export
    /// The complete review record for export (AC-4/FR-6): goals, ratings, notes, sign-off timestamps and the
    /// full immutable signature log. Returns DATA; PDF rendering is a deliberate seam (no PDF lib in repo).
    /// </summary>
    [HttpGet("reviews/cycles/{cycleId:guid}/employees/{employeeId:guid}/export")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<ReviewExportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(Guid cycleId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetReviewExportQuery(employeeId, cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ReviewExportDto>.Ok(result.Value!));
    }
}
