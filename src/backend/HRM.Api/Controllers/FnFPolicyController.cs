using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Application.Features.Payroll.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped endpoints for the effective-dated Full-and-Final settlement policy (ISSUE-294 Phase 1). The
/// policy governs which components a final settlement includes when an employee is offboarded. All operations
/// require <c>Payroll.Configure</c> (the same catalog permission the statutory-rule configuration uses). Phase 1
/// is API-only (no FE); every query is tenant-scoped via the EF global query filter.
/// </summary>
[ApiController]
[Route("api/v1/payroll/fnf-policy")]
[Authorize]
public sealed class FnFPolicyController : ControllerBase
{
    private readonly IMediator _mediator;

    public FnFPolicyController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET — lists the tenant's F&F policy versions, newest effective-from first.</summary>
    [HttpGet]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<FnFPolicyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListFnFPoliciesQuery(), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<FnFPolicyDto>>.Ok(result.Value!));
    }

    /// <summary>GET — the policy version in effect on a date (defaults to today); falls back to the code-default.</summary>
    [HttpGet("effective")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<FnFPolicyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEffective([FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
    {
        var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _mediator.Send(new GetEffectiveFnFPolicyQuery(date), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<FnFPolicyDto>.Ok(result.Value!));
    }

    /// <summary>POST — configures a new effective-dated policy version (never mutates history).</summary>
    [HttpPost]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<FnFPolicyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateFnFPolicyRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateFnFPolicyCommand(
            request.EffectiveFrom, request.IncludeProRatedFinalPay, request.IncludeStatutory,
            request.IncludeLeaveEncashment, request.FinalPeriodOwnedBySettlement, request.IsActive),
            cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : CreatedAtAction(nameof(GetEffective), new { asOf = result.Value!.EffectiveFrom }, ApiResponse<FnFPolicyDto>.Ok(result.Value!));
    }
}
