using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Auth.Commands;
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
    private readonly ICurrentUser _currentUser;

    public AdminTenantsController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

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

    /// <summary>
    /// POST /api/v1/system/tenants/{tenantId}/users/{userId}/unlock — BUG-046 (US-AUTH-010 BR-4):
    /// system-admin cross-tenant account unlock. The tenant-scoped tenant-admin unlock
    /// (<c>POST /api/v1/tenant/users/{id}/unlock</c>) cannot reach a locked user in another tenant, so a
    /// platform SystemAdmin needs a cross-tenant path. Runs in the SYSTEM/admin context (no resolved tenant)
    /// and operates ACROSS tenants: it reuses <see cref="UnlockUserCommand"/> → <c>IAuthService.UnlockUserAsync</c>,
    /// which resolves the target user + tenant membership via IgnoreQueryFilters and clears the lockout fields
    /// (LockedUntil / FailedLoginCount / MfaFailedAttemptCount), attributing the audit row to the acting admin.
    ///
    /// <para>Authorization is <c>Tenant.Provision</c> — the SystemAdmin-only gate this controller already uses;
    /// read-only System Support does NOT hold it, so it cannot perform this write. 403 when the caller lacks the
    /// permission or the target user has no membership in the specified tenant; 404 when the user does not exist.</para>
    /// </summary>
    [HttpPost("{tenantId:guid}/users/{userId:guid}/unlock")]
    [RequirePermission("Tenant.Provision")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockUser(
        Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UnlockUserCommand(userId, tenantId, _currentUser.UserId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse.Ok("User account unlocked successfully."));
    }

    /// <summary>
    /// PUT /api/v1/system/tenants/{id}/plan — move an existing tenant onto a different subscription plan
    /// (ISSUE-341). Writes <c>Tenant.PlanId</c>, recomputes <c>Tenant.EnabledModules</c> from the target plan
    /// (the same derivation as provisioning), invalidates the tenant's resolution cache, and audits
    /// "Tenant.PlanChanged". Returns the recomputed entitlement snapshot.
    ///
    /// <para>Authorization is <c>Plan.Manage</c> — the SystemAdmin-only gate the plan-management routes
    /// (<c>AdminPlansController</c>) already use, chosen over this controller's <c>Tenant.Provision</c> because
    /// this is a plan-management action; read-only System Support (which holds <c>Plan.View</c> only) is denied.
    /// 400 when the target plan is unknown/inactive; 403 for the system tenant (BR-2); 404 when the tenant is
    /// absent.</para>
    /// </summary>
    [HttpPut("{id:guid}/plan")]
    [RequirePermission("Plan.Manage")]
    [ProducesResponseType(typeof(ApiResponse<ChangeTenantPlanResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePlan(
        Guid id, [FromBody] ChangeTenantPlanRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ChangeTenantPlanCommand(id, request.PlanCode), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ChangeTenantPlanResultDto>.Ok(result.Value!));
    }
}
