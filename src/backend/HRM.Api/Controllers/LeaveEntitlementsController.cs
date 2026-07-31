using HRM.Application.DTOs;
using HRM.Application.Features.LeaveEntitlements.Commands;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Application.Features.LeaveEntitlements.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped leave entitlement configuration endpoints (US-LV-002).
/// Manages entitlement rules, per-employee overrides, and effective entitlement computation.
/// </summary>
[ApiController]
[Route("api/v1/tenant/leave-entitlements")]
[Authorize]
public sealed class LeaveEntitlementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveEntitlementsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ══════════════════════════════════════════════════════════════
    //  Rules
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/v1/tenant/leave-entitlements/rules
    /// Lists all entitlement rules, optionally filtered by leave type.
    /// </summary>
    [HttpGet("rules")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LeaveEntitlementRuleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRules(
        [FromQuery] Guid? leaveTypeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLeaveEntitlementRulesQuery(leaveTypeId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<LeaveEntitlementRuleDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/leave-entitlements/rules/{id}
    /// Gets a single entitlement rule by ID.
    /// </summary>
    [HttpGet("rules/{id:guid}")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<LeaveEntitlementRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRuleById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLeaveEntitlementRuleByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveEntitlementRuleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/leave-entitlements/rules
    /// Creates a new entitlement rule (AC-1).
    /// </summary>
    [HttpPost("rules")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<LeaveEntitlementRuleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRule(
        [FromBody] UpsertLeaveEntitlementRuleRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateLeaveEntitlementRuleCommand(
            request.LeaveTypeId, request.DepartmentId, request.JobTitleId,
            request.JobLevelId, request.EmploymentType,
            request.TenureMinMonths, request.TenureMaxMonths,
            request.EntitlementDays, request.Priority,
            request.EffectiveFrom, request.EffectiveTo);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetRuleById),
            new { id = result.Value!.Id },
            ApiResponse<LeaveEntitlementRuleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/leave-entitlements/rules/{id}
    /// Updates an existing entitlement rule (AC-5).
    /// </summary>
    [HttpPut("rules/{id:guid}")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<LeaveEntitlementRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRule(
        Guid id, [FromBody] UpsertLeaveEntitlementRuleRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateLeaveEntitlementRuleCommand(
            id, request.LeaveTypeId, request.DepartmentId, request.JobTitleId,
            request.JobLevelId, request.EmploymentType,
            request.TenureMinMonths, request.TenureMaxMonths,
            request.EntitlementDays, request.Priority,
            request.EffectiveFrom, request.EffectiveTo);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveEntitlementRuleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// DELETE /api/v1/tenant/leave-entitlements/rules/{id}
    /// Soft-deletes an entitlement rule.
    /// </summary>
    [HttpDelete("rules/{id:guid}")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteLeaveEntitlementRuleCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Leave entitlement rule deleted successfully."));
    }

    /// <summary>
    /// POST /api/v1/tenant/leave-entitlements/rules/bulk
    /// Bulk-creates entitlement rules (FR-4).
    /// </summary>
    [HttpPost("rules/bulk")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LeaveEntitlementRuleDto>>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkCreateRules(
        [FromBody] BulkCreateRulesRequest request, CancellationToken cancellationToken)
    {
        var command = new BulkCreateRulesCommand(request.Rules);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return StatusCode(201, ApiResponse<IReadOnlyList<LeaveEntitlementRuleDto>>.Ok(result.Value!));
    }

    // ══════════════════════════════════════════════════════════════
    //  Overrides
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/v1/tenant/leave-entitlements/overrides
    /// Lists overrides filtered by employee, leave type, and/or year.
    /// </summary>
    [HttpGet("overrides")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LeaveEntitlementOverrideDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverrides(
        [FromQuery] Guid? employeeId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] int? leaveYear,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetLeaveEntitlementOverridesQuery(employeeId, leaveTypeId, leaveYear), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<LeaveEntitlementOverrideDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/leave-entitlements/overrides
    /// Creates or updates a per-employee override (AC-3).
    /// </summary>
    [HttpPost("overrides")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<LeaveEntitlementOverrideDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertOverride(
        [FromBody] UpsertLeaveEntitlementOverrideRequest request, CancellationToken cancellationToken)
    {
        var command = new UpsertLeaveEntitlementOverrideCommand(
            request.EmployeeId, request.LeaveTypeId,
            request.LeaveYear, request.EntitlementDays, request.Reason);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveEntitlementOverrideDto>.Ok(result.Value!));
    }

    /// <summary>
    /// DELETE /api/v1/tenant/leave-entitlements/overrides/{id}
    /// Soft-deletes a per-employee override.
    /// </summary>
    [HttpDelete("overrides/{id:guid}")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOverride(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteLeaveEntitlementOverrideCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Leave entitlement override deleted successfully."));
    }

    // ══════════════════════════════════════════════════════════════
    //  Effective Entitlement
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/v1/tenant/leave-entitlements/effective
    /// Computes effective entitlement for an employee, leave type, and year.
    /// </summary>
    [HttpGet("effective")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<EffectiveEntitlementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ComputeEffective(
        [FromQuery] Guid employeeId,
        [FromQuery] Guid leaveTypeId,
        [FromQuery] int leaveYear,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ComputeEffectiveEntitlementQuery(employeeId, leaveTypeId, leaveYear), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<EffectiveEntitlementDto>.Ok(result.Value!));
    }

    // ══════════════════════════════════════════════════════════════
    //  BUG-291 exposure report (READ-ONLY)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/v1/tenant/leave-entitlements/accrual-over-credit-exposure?asOfDate=YYYY-MM-DD
    /// BUG-291 exposure report (READ-ONLY). Lists, for the CURRENT tenant, every (employee × leave type) whose
    /// legacy full-year accrual over-credited relative to its configured Monthly/Quarterly accrual frequency as
    /// of <paramref name="asOfDate"/>. Reports the affected population only — it adjusts nothing (correcting an
    /// over-credit downward is an employee-detriment decision made case-by-case).
    ///
    /// <para><b>Authorization.</b> Gated by <c>Leave.ConfigurePolicy</c> — the platform's admin-level
    /// leave-entitlement/accrual configuration permission that already gates every sibling entitlement route on
    /// this controller. A true system/admin-context route was not used because this report runs in the RESOLVED
    /// tenant under the normal query filters (system routes run tenant-less with IgnoreQueryFilters); this is
    /// the admin gate that is both elevated and tenant-scoped.</para>
    /// </summary>
    [HttpGet("accrual-over-credit-exposure")]
    [RequirePermission("Leave.ConfigurePolicy")]
    [ProducesResponseType(typeof(ApiResponse<AccrualOverCreditExposureReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAccrualOverCreditExposure(
        [FromQuery] DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAccrualOverCreditExposureQuery(asOfDate), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<AccrualOverCreditExposureReportDto>.Ok(result.Value!));
    }
}
