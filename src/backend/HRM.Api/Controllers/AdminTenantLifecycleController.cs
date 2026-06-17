using HRM.Application.DTOs;
using HRM.Application.Features.Tenants.Commands;
using HRM.Application.Features.Tenants.DTOs;
using HRM.Application.Features.Tenants.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// System Admin Console — tenant lifecycle: suspend / terminate / reactivate / restore + lifecycle history
/// (US-ADM-004). These endpoints run in the SYSTEM/admin context (admin.* subdomain → IsSystemContext, no
/// resolved tenant) and operate ACROSS tenants; the service uses IgnoreQueryFilters with explicit id scoping.
///
/// <para><b>Authorization (BR-7).</b> The mutating transitions are gated by the destructive
/// <c>Tenant.Lifecycle</c> permission — only the platform SystemAdmin role holds it (seeded with every catalog
/// permission); System Support does NOT. The lifecycle-history GET is gated by <c>Tenant.ViewLifecycle</c>,
/// which both SystemAdmin and the read-only System Support role hold. The codebase's JWT carries roles in a
/// custom claim, so the established permission-policy gate is used rather than <c>[Authorize(Roles=...)]</c> —
/// same decision as US-ADM-001's AdminTenantsController.</para>
/// </summary>
[ApiController]
[Route("api/v1/system/tenants/{tenantId:guid}/lifecycle")]
[Authorize]
public sealed class AdminTenantLifecycleController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminTenantLifecycleController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// POST /api/v1/system/tenants/{tenantId}/lifecycle/suspend — suspend a tenant (AC-1/FR-1). Body: { reason }.
    /// </summary>
    [HttpPost("suspend")]
    [RequirePermission("Tenant.Lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<TenantLifecycleResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Suspend(
        [FromRoute] Guid tenantId, [FromBody] TenantReasonRequest request, CancellationToken cancellationToken)
        => Dispatch(new SuspendTenantCommand(tenantId, request.Reason), cancellationToken);

    /// <summary>
    /// POST /api/v1/system/tenants/{tenantId}/lifecycle/terminate — terminate into grace (AC-3/FR-2).
    /// Body: { reason, graceDays }.
    /// </summary>
    [HttpPost("terminate")]
    [RequirePermission("Tenant.Lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<TenantLifecycleResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Terminate(
        [FromRoute] Guid tenantId, [FromBody] TerminateTenantRequest request, CancellationToken cancellationToken)
        => Dispatch(new TerminateTenantCommand(tenantId, request.Reason, request.GraceDays), cancellationToken);

    /// <summary>
    /// POST /api/v1/system/tenants/{tenantId}/lifecycle/reactivate — reactivate a suspended tenant (AC-5/FR-5).
    /// </summary>
    [HttpPost("reactivate")]
    [RequirePermission("Tenant.Lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<TenantLifecycleResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reactivate([FromRoute] Guid tenantId, CancellationToken cancellationToken)
        => Dispatch(new ReactivateTenantCommand(tenantId), cancellationToken);

    /// <summary>
    /// POST /api/v1/system/tenants/{tenantId}/lifecycle/restore — restore a terminating tenant within grace (AC-6/FR-6).
    /// </summary>
    [HttpPost("restore")]
    [RequirePermission("Tenant.Lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<TenantLifecycleResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Restore([FromRoute] Guid tenantId, CancellationToken cancellationToken)
        => Dispatch(new RestoreTenantCommand(tenantId), cancellationToken);

    /// <summary>
    /// GET /api/v1/system/tenants/{tenantId}/lifecycle/history — lifecycle-event timeline, newest first.
    /// Viewable by System Admin AND System Support (BR-7).
    /// </summary>
    [HttpGet("history")]
    [RequirePermission("Tenant.ViewLifecycle")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TenantLifecycleEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History([FromRoute] Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantLifecycleHistoryQuery(tenantId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<TenantLifecycleEventDto>>.Ok(result.Value!));
    }

    private async Task<IActionResult> Dispatch(
        IRequest<Application.Common.Models.Result<TenantLifecycleResultDto>> command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TenantLifecycleResultDto>.Ok(result.Value!));
    }
}

/// <summary>Request body for suspend (US-ADM-004 AC-1). Tenant id comes from the route.</summary>
public sealed record TenantReasonRequest(string Reason);

/// <summary>Request body for terminate (US-ADM-004 AC-3). Tenant id comes from the route.</summary>
public sealed record TerminateTenantRequest(string Reason, int GraceDays);
