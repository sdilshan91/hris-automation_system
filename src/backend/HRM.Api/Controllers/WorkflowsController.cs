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
/// US-ADM-007: Tenant-scoped approval-workflow DEFINITION management for a Tenant Admin / Tenant Owner (BR-1).
/// All operations target ONLY the current tenant via ITenantContext + the EF global query filter (BR-7); there
/// is no tenant id in any route or body. Reads require <c>Tenant.ViewWorkflows</c>; writes require
/// <c>Tenant.ManageWorkflows</c> (both held by Tenant Admin and Tenant Owner).
///
/// <para>This is the DEFINITION/configuration layer. The RUNTIME engine (live request routing through these
/// definitions, SLA timers + escalation, live delegation, workflow instances) is DEFERRED.</para>
/// </summary>
[ApiController]
[Route("api/v1/tenant/workflows")]
[Authorize]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkflowsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/tenant/workflows — list the current tenant's workflows, ordered by entity type (AC-1).</summary>
    [HttpGet]
    [RequirePermission("Tenant.ViewWorkflows")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<WorkflowListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListWorkflowsQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<WorkflowListItemDto>>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/workflows/{id} — get one workflow definition (version) with its steps.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Tenant.ViewWorkflows")]
    [ProducesResponseType(typeof(ApiResponse<WorkflowDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkflowQuery(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<WorkflowDetailDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/workflows/{lineageId}/instances — paged list of the runtime instances for a workflow
    /// definition lineage (US-ADM-011c FR-10). Newest first; pageSize clamped 1..50 (default 20), page 1-based.
    /// </summary>
    [HttpGet("{lineageId:guid}/instances")]
    [RequirePermission("Tenant.ManageWorkflows")]
    [ProducesResponseType(typeof(ApiResponse<PagedWorkflowInstancesDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInstances(
        Guid lineageId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListWorkflowInstancesQuery(lineageId, page, pageSize), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PagedWorkflowInstancesDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/workflows — create a new workflow definition (v1) (AC-2/FR-1).</summary>
    [HttpPost]
    [RequirePermission("Tenant.ManageWorkflows")]
    [ProducesResponseType(typeof(ApiResponse<WorkflowDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateWorkflowCommand(request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<WorkflowDetailDto>.Ok(result.Value!, "Workflow created."));
    }

    /// <summary>
    /// PUT /api/v1/tenant/workflows/{lineageId} — edit a workflow; creates a new version when active (AC-3/FR-3).
    /// The path id is the workflow's LINEAGE id (stable across versions).
    /// </summary>
    [HttpPut("{lineageId:guid}")]
    [RequirePermission("Tenant.ManageWorkflows")]
    [ProducesResponseType(typeof(ApiResponse<WorkflowDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid lineageId, [FromBody] UpdateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateWorkflowCommand(lineageId, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<WorkflowDetailDto>.Ok(result.Value!, "Workflow updated."));
    }

    /// <summary>POST /api/v1/tenant/workflows/{id}/archive — archive a workflow (FR-6).</summary>
    [HttpPost("{id:guid}/archive")]
    [RequirePermission("Tenant.ManageWorkflows")]
    [ProducesResponseType(typeof(ApiResponse<WorkflowDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ArchiveWorkflowCommand(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<WorkflowDetailDto>.Ok(result.Value!, "Workflow archived."));
    }

    /// <summary>POST /api/v1/tenant/workflows/{id}/restore — restore an archived workflow (FR-6/BR-2).</summary>
    [HttpPost("{id:guid}/restore")]
    [RequirePermission("Tenant.ManageWorkflows")]
    [ProducesResponseType(typeof(ApiResponse<WorkflowDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RestoreWorkflowCommand(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<WorkflowDetailDto>.Ok(result.Value!, "Workflow restored."));
    }

    /// <summary>DELETE /api/v1/tenant/workflows/{id} — delete a workflow (BR-6; only when no in-flight instances).</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Tenant.ManageWorkflows")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteWorkflowCommand(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse.Ok("Workflow deleted."));
    }
}
