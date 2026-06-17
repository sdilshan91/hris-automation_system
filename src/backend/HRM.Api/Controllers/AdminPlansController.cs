using HRM.Application.DTOs;
using HRM.Application.Features.SubscriptionPlans.Commands;
using HRM.Application.Features.SubscriptionPlans.DTOs;
using HRM.Application.Features.SubscriptionPlans.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// System Admin Console — subscription-plan management (US-ADM-009). These endpoints run in the SYSTEM/admin
/// context (admin.* subdomain → IsSystemContext, no resolved tenant) and operate ACROSS tenants; plans and
/// per-tenant limit overrides are platform tables (not tenant-scoped).
///
/// <para>Authorization (BR-1): WRITES require <c>Plan.Manage</c> — held by the platform SystemAdmin role only.
/// READS require <c>Plan.View</c> OR <c>Plan.Manage</c> — so the read-only System Support role (granted
/// <c>Plan.View</c>) can browse plans but cannot mutate them. The codebase carries roles in a custom "roles"
/// claim, so the established permission-policy gate is used rather than <c>[Authorize(Roles=...)]</c>.</para>
/// </summary>
[ApiController]
[Route("api/v1/system/plans")]
[Authorize]
public sealed class AdminPlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminPlansController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/system/plans — list all plans with active-tenant counts (AC-1/FR-5).</summary>
    [HttpGet]
    [RequirePermission("Plan.View", "Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PlanListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListPlansQuery(), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PlanListItemDto>>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/system/plans/{id} — full plan detail (AC-2/FR-2).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Plan.View", "Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse<PlanDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPlanQuery(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PlanDetailDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/system/plans — create a plan (AC-2/FR-2/FR-3). 409 on duplicate code.</summary>
    [HttpPost]
    [RequirePermission("Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse<CreatePlanResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : StatusCode(StatusCodes.Status201Created, ApiResponse<CreatePlanResultDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/system/plans/{id} — update a plan's editable fields (AC-3). The code is IMMUTABLE and is not
    /// part of the request body.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] PlanEditableFields fields, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdatePlanCommand(id, fields), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse.Ok());
    }

    /// <summary>POST /api/v1/system/plans/{id}/archive — archive a plan: IsActive=false (AC-4).</summary>
    [HttpPost("{id:guid}/archive")]
    [RequirePermission("Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ArchivePlanCommand(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse.Ok());
    }

    /// <summary>
    /// DELETE /api/v1/system/plans/{id} — delete a plan (FR-7/BR-5). 409 when any tenant references the plan.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeletePlanCommand(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse.Ok());
    }

    // ── Per-tenant plan-limit overrides (AC-5/FR-4) ──────────────────────────────

    /// <summary>
    /// GET /api/v1/system/plans/overrides?tenantId={id} — list a tenant's plan-limit overrides (AC-5/FR-4).
    /// Accessible from the system-admin tenant detail view.
    /// </summary>
    [HttpGet("overrides")]
    [RequirePermission("Plan.View", "Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PlanLimitOverrideDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListOverrides(
        [FromQuery] Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListPlanLimitOverridesQuery(tenantId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PlanLimitOverrideDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/system/plans/overrides — create/update a per-tenant plan-limit override (AC-5/FR-4).
    /// <c>value</c> null = unlimited override.
    /// </summary>
    [HttpPost("overrides")]
    [RequirePermission("Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse<PlanLimitOverrideDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertOverride(
        [FromBody] UpsertPlanLimitOverrideCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PlanLimitOverrideDto>.Ok(result.Value!));
    }

    /// <summary>DELETE /api/v1/system/plans/overrides/{overrideId} — remove a plan-limit override (AC-5).</summary>
    [HttpDelete("overrides/{overrideId:guid}")]
    [RequirePermission("Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveOverride(Guid overrideId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RemovePlanLimitOverrideCommand(overrideId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse.Ok());
    }
}
