using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Workflows.Commands;
using HRM.Application.Features.Workflows.DTOs;
using HRM.Application.Features.Workflows.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-ADM-011 (Phase 1) FR-12: approver decisions on live approval-workflow INSTANCES. Authorization is
/// DYNAMIC (AC-10/Q5) — the runtime enforces that only the resolved approver of the current Pending step may
/// decide, so this endpoint requires only authentication (no static permission). All operations are
/// tenant-scoped via ITenantContext + the EF global query filter (AC-9); there is no tenant id in the route.
///
/// <para>Phase 1 wires the <c>Leave</c> entity type: a decision is routed to the Leave service, which drives
/// the runtime and applies the leave ledger/status atomically. Read APIs for the step chain, the SLA job, and
/// the remaining entity types are US-ADM-011b/011c.</para>
/// </summary>
[ApiController]
[Route("api/v1/tenant/workflow-instances")]
[Authorize]
public sealed class WorkflowInstancesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkflowInstancesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Request body for a step decision.</summary>
    public sealed record WorkflowDecisionBody(string? Comment);

    /// <summary>
    /// GET /api/v1/tenant/workflow-instances/{instanceId} — the live instance with its ordered step chain
    /// (US-ADM-011c FR-12). Tenant-scoped by the global query filter (AC-9). ISSUE-267: the step chain exposes
    /// approver identities, decisions, and comments, so the read is gated by <c>Tenant.ViewWorkflows</c>
    /// (held by Tenant Admin/Owner — the same permission that gates the workflow-definition reads) rather than
    /// being open to any authenticated tenant user.
    /// </summary>
    [HttpGet("{instanceId:guid}")]
    [RequirePermission("Tenant.ViewWorkflows")]
    [ProducesResponseType(typeof(ApiResponse<WorkflowInstanceDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkflowInstanceQuery(instanceId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<WorkflowInstanceDetailDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/workflow-instances/{instanceId}/approve — approve the active step (AC-2).</summary>
    [HttpPost("{instanceId:guid}/approve")]
    [ProducesResponseType(typeof(ApiResponse<WorkflowDecisionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        [FromRoute] Guid instanceId,
        [FromBody] WorkflowDecisionBody? body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DecideWorkflowInstanceCommand(instanceId, WorkflowDecisionAction.Approve, body?.Comment), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<WorkflowDecisionResponseDto>.Ok(result.Value!, "Step approved."));
    }

    /// <summary>POST /api/v1/tenant/workflow-instances/{instanceId}/reject — reject the active step (AC-2/BR-2).</summary>
    [HttpPost("{instanceId:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<WorkflowDecisionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        [FromRoute] Guid instanceId,
        [FromBody] WorkflowDecisionBody body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DecideWorkflowInstanceCommand(instanceId, WorkflowDecisionAction.Reject, body?.Comment), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<WorkflowDecisionResponseDto>.Ok(result.Value!, "Step rejected."));
    }
}
