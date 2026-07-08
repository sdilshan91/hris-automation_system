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
/// Tenant-scoped endpoints for Performance Improvement Plans (US-PRF-008). Create / set-outcome / confirm-
/// escalation require <c>Performance.Review.All</c> (HR, BR-1). Record-checkpoint admits
/// <c>Performance.Review.Team</c> (the employee's manager) OR <c>Performance.Review.All</c> (HR) — the service
/// enforces the manager↔direct-report link (AC-3). Acknowledge requires <c>Performance.Read.Self</c> and the
/// service enforces the caller IS the PIP's employee (BR-4). List/Get admit any performance-review permission
/// and the service applies the FR-8 visibility restriction (employee / manager / HR / mentor only). The client
/// IP is captured here for the FR-5 audit. All operations are tenant-scoped via the EF global query filter (NFR-2).
///
/// BR-5: PIP data is INTENTIONALLY served only from these endpoints, never folded into the general performance
/// dashboard (US-PRF-007) — the dashboard service does not query the PIP tables.
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance/pips")]
[Authorize]
public sealed class PipController : ControllerBase
{
    private readonly IMediator _mediator;

    public PipController(IMediator mediator) => _mediator = mediator;

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult PipResult(HRM.Application.Common.Models.Result<PipDto> result)
    {
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<PipDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/performance/pips — the PIP list, scoped to the caller's visibility (AC-1/FR-8/BR-5).</summary>
    [HttpGet]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PipSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListPipsQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<PipSummaryDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/pips/draft?employeeId={id?}&amp;reviewId={id?} — the create-form pre-fill
    /// payload (AC-1). HR-only (same BR-1 gate as create). Both query params are optional: omit employeeId for a
    /// blank HR-initiated form (escalation options only); pass reviewId to seed the reason from a flagged
    /// manager review (US-PRF-003). 404 if a supplied employeeId is not in this tenant.
    /// </summary>
    [HttpGet("draft")]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<PipDraftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDraft(
        [FromQuery] Guid? employeeId, [FromQuery] Guid? reviewId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPipDraftQuery(employeeId, reviewId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<PipDraftDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/pips/{pipId} — one full PIP with its immutable history (AC-1/FR-5).
    /// VISIBILITY-restricted: only the employee, their manager, HR or the assigned mentor (FR-8) — else 403.
    /// </summary>
    [HttpGet("{pipId:guid}")]
    [RequirePermission("Performance.Read.Self", "Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<PipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid pipId, CancellationToken cancellationToken)
        => PipResult(await _mediator.Send(new GetPipQuery(pipId), cancellationToken));

    /// <summary>
    /// POST /api/v1/tenant/performance/pips — create &amp; initiate a PIP (AC-1/AC-2/FR-1). HR-only (BR-1).
    /// Enforces BR-2 (one active PIP) and BR-3 (≥30 days); notifies employee/manager/mentor; schedules reminders.
    /// </summary>
    [HttpPost]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<PipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreatePipRequest request, CancellationToken cancellationToken)
    {
        var input = new CreatePipInput(
            request.EmployeeId, request.Reason, request.StartDate, request.EndDate, request.MentorEmployeeId,
            request.OriginManagerReviewId, request.EscalationAction, request.Objectives ?? [],
            request.CheckpointDates ?? [], ClientIp);
        return PipResult(await _mediator.Send(new CreatePipCommand(input), cancellationToken));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/pips/{pipId}/acknowledge — employee acknowledges PIP initiation (BR-4).
    /// Caller must be the PIP's employee (enforced server-side). Records an immutable acknowledgement event.
    /// </summary>
    [HttpPost("{pipId:guid}/acknowledge")]
    [RequirePermission("Performance.Read.Self")]
    [ProducesResponseType(typeof(ApiResponse<PipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Acknowledge(Guid pipId, CancellationToken cancellationToken)
        => PipResult(await _mediator.Send(new AcknowledgePipCommand(new AcknowledgePipInput(pipId, ClientIp)), cancellationToken));

    /// <summary>
    /// POST /api/v1/tenant/performance/pips/{pipId}/checkpoints — record an append-only checkpoint assessment
    /// (AC-3/FR-4). Manager (direct report) OR HR. Notifies the employee. The recorded row is immutable (FR-5).
    /// </summary>
    [HttpPost("{pipId:guid}/checkpoints")]
    [RequirePermission("Performance.Review.Team", "Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<PipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordCheckpoint(
        Guid pipId, [FromBody] RecordCheckpointRequest request, CancellationToken cancellationToken)
    {
        var input = new RecordCheckpointInput(
            pipId, request.CheckpointDate, request.ProgressStatus, request.EvidenceNotes,
            request.AttachmentFileName, request.AttachmentStorageKey, request.AttachmentContentType,
            request.AttachmentSizeBytes, ClientIp);
        return PipResult(await _mediator.Send(new RecordPipCheckpointCommand(input), cancellationToken));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/pips/{pipId}/outcome — set the final outcome (AC-4). HR-ONLY (BR-1 — a
    /// manager cannot close/extend). SuccessfullyCompleted | Extended (new end date + optional objectives, FR-6)
    /// | NotMet (triggers escalation, AC-5).
    /// </summary>
    [HttpPost("{pipId:guid}/outcome")]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<PipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetOutcome(
        Guid pipId, [FromBody] SetPipOutcomeRequest request, CancellationToken cancellationToken)
    {
        var input = new SetPipOutcomeInput(
            pipId, request.Outcome, request.Notes, request.NewEndDate, request.NewObjectives, ClientIp);
        return PipResult(await _mediator.Send(new SetPipOutcomeCommand(input), cancellationToken));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/pips/{pipId}/escalation — confirm the escalation for a Not-Met PIP
    /// (AC-5/BR-6). HR-ONLY. Records the decision + notifies stakeholders + writes an immutable audit event (FR-5).
    /// </summary>
    [HttpPost("{pipId:guid}/escalation")]
    [RequirePermission("Performance.Review.All")]
    [ProducesResponseType(typeof(ApiResponse<PipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmEscalation(
        Guid pipId, [FromBody] ConfirmEscalationRequest request, CancellationToken cancellationToken)
    {
        var input = new ConfirmEscalationInput(pipId, request.EscalationAction, request.Notes, ClientIp);
        return PipResult(await _mediator.Send(new ConfirmPipEscalationCommand(input), cancellationToken));
    }
}
