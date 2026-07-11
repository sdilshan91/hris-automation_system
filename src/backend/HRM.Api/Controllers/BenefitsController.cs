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

    // ── US-TRN-003: eligibility rules ────────────────────────────────────────────

    /// <summary>POST .../plans/{planId}/eligibility-rules — defines an eligibility rule (AC-1).</summary>
    [HttpPost("plans/{planId:guid}/eligibility-rules")]
    [RequirePermission(PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<EligibilityRuleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddEligibilityRule(Guid planId, [FromBody] CreateEligibilityRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AddEligibilityRuleCommand(planId, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return StatusCode(StatusCodes.Status201Created, ApiResponse<EligibilityRuleDto>.Ok(result.Value!));
    }

    /// <summary>GET .../plans/{planId}/eligibility-rules — lists a plan's eligibility rules (AC-1).</summary>
    [HttpGet("plans/{planId:guid}/eligibility-rules")]
    [RequirePermission(PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EligibilityRuleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEligibilityRules(Guid planId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEligibilityRulesQuery(planId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<EligibilityRuleDto>>.Ok(result.Value!));
    }

    /// <summary>DELETE .../eligibility-rules/{id} — removes an eligibility rule (AC-1).</summary>
    [HttpDelete("eligibility-rules/{id:guid}")]
    [RequirePermission(PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteEligibilityRule(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteEligibilityRuleCommand(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<bool>.Ok(result.Value));
    }

    // ── US-TRN-003: eligible plans ───────────────────────────────────────────────

    /// <summary>GET .../eligible — plans the current user's employee qualifies for (AC-2/AC-8).</summary>
    [HttpGet("eligible")]
    [RequirePermission(PermissionCatalog.Benefits.ViewOwn, PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EligiblePlanDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyEligiblePlans(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyEligiblePlansQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<EligiblePlanDto>>.Ok(result.Value!));
    }

    /// <summary>GET .../employees/{employeeId}/eligible — a given employee's eligible plans (self or View.All) (AC-8).</summary>
    [HttpGet("employees/{employeeId:guid}/eligible")]
    [RequirePermission(PermissionCatalog.Benefits.ViewOwn, PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EligiblePlanDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEmployeeEligiblePlans(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeEligiblePlansQuery(employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<EligiblePlanDto>>.Ok(result.Value!));
    }

    // ── US-TRN-003: enrollment ───────────────────────────────────────────────────

    /// <summary>POST .../enrollments — enroll self (View.Own) or another employee (Manage) (AC-3/AC-4/AC-5/AC-6).</summary>
    [HttpPost("enrollments")]
    [RequirePermission(PermissionCatalog.Benefits.ViewOwn, PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<BenefitEnrollmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Enroll([FromBody] EnrollRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new EnrollBenefitCommand(request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return StatusCode(StatusCodes.Status201Created, ApiResponse<BenefitEnrollmentDto>.Ok(result.Value!));
    }

    /// <summary>POST .../enrollments/{id}/terminate — terminate an enrollment (own or Manage) (AC-7).</summary>
    [HttpPost("enrollments/{id:guid}/terminate")]
    [RequirePermission(PermissionCatalog.Benefits.ViewOwn, PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<BenefitEnrollmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TerminateEnrollment(Guid id, [FromBody] TerminateEnrollmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new TerminateEnrollmentCommand(id, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<BenefitEnrollmentDto>.Ok(result.Value!));
    }

    /// <summary>GET .../me/enrollments — the current user's employee's enrollments (AC-8).</summary>
    [HttpGet("me/enrollments")]
    [RequirePermission(PermissionCatalog.Benefits.ViewOwn, PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BenefitEnrollmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyEnrollments(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyEnrollmentsQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<BenefitEnrollmentDto>>.Ok(result.Value!));
    }

    /// <summary>GET .../employees/{employeeId}/enrollments — an employee's enrollments (self or View.All) (AC-8).</summary>
    [HttpGet("employees/{employeeId:guid}/enrollments")]
    [RequirePermission(PermissionCatalog.Benefits.ViewOwn, PermissionCatalog.Benefits.ViewAll, PermissionCatalog.Benefits.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BenefitEnrollmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEmployeeEnrollments(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeEnrollmentsQuery(employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<BenefitEnrollmentDto>>.Ok(result.Value!));
    }
}
