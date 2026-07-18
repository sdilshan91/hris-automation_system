using HRM.Application.DTOs;
using HRM.Application.Features.Impersonation.Commands;
using HRM.Application.Features.Impersonation.DTOs;
using HRM.Application.Features.Impersonation.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// System Admin Console — tenant-user impersonation (US-ADM-003). Initiate / end a session + list targets. These
/// run in the SYSTEM/admin context (admin.* subdomain → IsSystemContext, no resolved tenant) and operate ACROSS
/// tenants.
///
/// <para>Authorization is the <c>Impersonation.Initiate</c> permission (BR-1): held by the platform SystemAdmin
/// role (seeded with every catalog permission) AND the read-only System Support role. The codebase carries roles
/// in a custom "roles" claim (not ClaimTypes.Role), so the established permission-policy gate is used rather than
/// <c>[Authorize(Roles=...)]</c>. Whether a started session is READ-ONLY is decided server-side (System Support ⇒
/// read-only; Suspended target tenant ⇒ read-only), not by which permission the caller holds.</para>
/// </summary>
[ApiController]
[Route("api/v1/system/impersonation")]
[Authorize]
public sealed class AdminImpersonationController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminImpersonationController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// POST /api/v1/system/impersonation — start an impersonation session (AC-1/FR-1). Returns the impersonation
    /// JWT, redirect URL and session id. 400 invalid reason, 403 terminated/system target / read-only rules,
    /// 404 unknown tenant/user/membership, 409 an active session already exists (BR-3).
    /// </summary>
    [HttpPost]
    [RequirePermission("Impersonation.Initiate")]
    [ProducesResponseType(typeof(ApiResponse<StartImpersonationResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(
        [FromBody] StartImpersonationCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created, ApiResponse<StartImpersonationResultDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/system/impersonation/{sessionId}/end — end an active session (AC-3). Callable both from the
    /// admin console and from within the impersonated tenant UI (the banner's "End Session" sends the
    /// impersonation token); the read-only pipeline gate allowlists it.
    /// </summary>
    [HttpPost("{sessionId:guid}/end")]
    [RequirePermission("Impersonation.Initiate")]
    [ProducesResponseType(typeof(ApiResponse<EndImpersonationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> End(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new EndImpersonationCommand(sessionId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<EndImpersonationResultDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/system/impersonation/targets?tenantId={id} — list a tenant's active users for the picker
    /// (AC-1). Excludes system-admin / system-tenant users (BR-2).
    /// </summary>
    [HttpGet("targets")]
    [RequirePermission("Impersonation.Initiate")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ImpersonationTargetDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListTargets(
        [FromQuery] Guid tenantId, CancellationToken cancellationToken)
    {
        // ISSUE-001: a missing (or empty) required tenantId binds to Guid.Empty. That is a malformed request →
        // 400, not 404: 404 (tenant_not_found) is reserved for a SUPPLIED-but-unknown tenant id below.
        if (tenantId == Guid.Empty)
            return BadRequest(ApiResponse.Fail("The 'tenantId' query parameter is required.", "missing_required_parameter"));

        var result = await _mediator.Send(new ListImpersonationTargetsQuery(tenantId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<ImpersonationTargetDto>>.Ok(result.Value!));
    }
}
