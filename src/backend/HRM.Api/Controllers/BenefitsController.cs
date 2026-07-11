using HRM.Application.DTOs;
using HRM.Application.Features.Benefits.Commands;
using HRM.Application.Features.Benefits.DTOs;
using HRM.Application.Features.Benefits.Queries;
using HRM.Domain.Authorization;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped benefit-plan administration endpoints (US-TRN-002). Reads require any Benefits.View.*/Manage
/// permission; writes (create/edit/status) require Benefits.Manage. All operations are tenant-scoped via the EF
/// global query filter. Plans are archived (status → Archived), never hard-deleted (BR-4/AC-6) — there is no
/// delete endpoint.
/// </summary>
[ApiController]
[Route("api/v1/tenant/benefits")]
[Authorize]
public sealed class BenefitsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BenefitsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>GET /api/v1/tenant/benefits/plans — lists the tenant's plans (AC-5).</summary>
    [HttpGet("plans")]
    [RequirePermission(PermissionCatalog.Benefits.ViewOwn, PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BenefitPlanDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBenefitPlansQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<BenefitPlanDto>>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/benefits/plans/{id} — one plan (AC-5).</summary>
    [HttpGet("plans/{id:guid}")]
    [RequirePermission(PermissionCatalog.Benefits.ViewOwn, PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<BenefitPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBenefitPlanByIdQuery(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<BenefitPlanDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/benefits/plans — creates a Draft plan (AC-1).</summary>
    [HttpPost("plans")]
    [RequirePermission(PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<BenefitPlanDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePlan([FromBody] CreateBenefitPlanRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateBenefitPlanCommand(request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return CreatedAtAction(nameof(GetPlanById), new { id = result.Value!.Id }, ApiResponse<BenefitPlanDto>.Ok(result.Value!));
    }

    /// <summary>PUT /api/v1/tenant/benefits/plans/{id} — updates plan metadata/cost/coverage (AC-4).</summary>
    [HttpPut("plans/{id:guid}")]
    [RequirePermission(PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<BenefitPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] UpdateBenefitPlanRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateBenefitPlanCommand(id, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<BenefitPlanDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/benefits/plans/{id}/status — activate/deactivate/archive (AC-2/AC-3/AC-6).
    /// Illegal transition → 409.</summary>
    [HttpPost("plans/{id:guid}/status")]
    [RequirePermission(PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<BenefitPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePlanStatus(Guid id, [FromBody] ChangeBenefitPlanStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ChangeBenefitPlanStatusCommand(id, request.Status), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<BenefitPlanDto>.Ok(result.Value!));
    }
}
