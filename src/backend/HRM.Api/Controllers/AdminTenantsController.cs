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
/// System Admin Console — tenant provisioning + tenant directory (US-ADM-001). These endpoints run in the
/// SYSTEM/admin context (admin.* subdomain → IsSystemContext, no resolved tenant) and operate ACROSS tenants;
/// the service uses IgnoreQueryFilters deliberately and creates Tenant/User/SubscriptionPlan rows while no
/// tenant is resolved.
///
/// <para>Authorization is <c>Tenant.Provision</c> (BR-1): only the platform SystemAdmin role holds it (it is
/// seeded with every catalog permission). SystemSupport is NOT granted it, so it cannot provision tenants.
/// The codebase's JWT carries roles in a custom "roles" claim (not ClaimTypes.Role), so the established
/// permission-policy gate is used rather than <c>[Authorize(Roles=...)]</c>.</para>
/// </summary>
[ApiController]
[Route("api/v1/system/tenants")]
[Authorize]
public sealed class AdminTenantsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminTenantsController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/system/tenants — provision a new tenant (AC-1). System Admin only (BR-1).</summary>
    [HttpPost]
    [RequirePermission("Tenant.Provision")]
    [ProducesResponseType(typeof(ApiResponse<ProvisionTenantResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Provision(
        [FromBody] ProvisionTenantCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created, ApiResponse<ProvisionTenantResultDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/system/tenants — the System Admin tenant directory (AC-4). System Admin only.
    /// Optional <c>?search=</c> filters case-insensitively on tenant name/subdomain (US-ADM-002 AC-2).</summary>
    [HttpGet]
    [RequirePermission("Tenant.Provision")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TenantListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListTenantsQuery(search), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<TenantListItemDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/system/tenants/subdomain-availability?subdomain=acme — debounced FE availability check
    /// (FR-2). Returns { available, reason? }. System Admin only.
    /// </summary>
    [HttpGet("subdomain-availability")]
    [RequirePermission("Tenant.Provision")]
    [ProducesResponseType(typeof(ApiResponse<SubdomainAvailabilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CheckSubdomain(
        [FromQuery] string subdomain, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CheckSubdomainAvailabilityQuery(subdomain), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<SubdomainAvailabilityDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/system/tenants/plans — active subscription plans for the Create Tenant form.</summary>
    [HttpGet("plans")]
    [RequirePermission("Tenant.Provision")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubscriptionPlanDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListPlans(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListActivePlansQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<SubscriptionPlanDto>>.Ok(result.Value!));
    }
}
