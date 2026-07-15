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
/// Tenant-scoped endpoints for the effective-dated payroll CALENDAR policy (US-ATT-011 AC-4 / CAL-5). The policy
/// governs whether public holidays are excluded from the payroll working-days count — which is the pro-ration
/// denominator, the LOP daily-rate divisor and the overtime hourly-rate divisor. All operations require
/// <c>Payroll.Configure</c> (the same catalog permission the F&amp;F / statutory-rule configuration uses); every
/// query is tenant-scoped via the EF global query filter.
///
/// <para>The flag defaults to FALSE and is effective-dated, so configuring it applies from the chosen date
/// forward and never rewrites a period whose figures were already computed.</para>
/// </summary>
[ApiController]
[Route("api/v1/payroll/calendar-policy")]
[Authorize]
public sealed class PayrollCalendarPolicyController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayrollCalendarPolicyController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET — lists the tenant's calendar-policy versions, newest effective-from first.</summary>
    [HttpGet]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollCalendarPolicyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListPayrollCalendarPoliciesQuery(), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PayrollCalendarPolicyDto>>.Ok(result.Value!));
    }

    /// <summary>GET — the policy version in effect on a date (defaults to today); falls back to the code-default.</summary>
    [HttpGet("effective")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<PayrollCalendarPolicyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEffective([FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
    {
        var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _mediator.Send(new GetEffectivePayrollCalendarPolicyQuery(date), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollCalendarPolicyDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST — configures a new effective-dated policy version (never mutates history). Re-configuring the SAME
    /// effective date REPLACES that version, so resolution never tie-breaks between two versions on one date.
    /// </summary>
    [HttpPost]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<PayrollCalendarPolicyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePayrollCalendarPolicyRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreatePayrollCalendarPolicyCommand(
            request.EffectiveFrom, request.ExcludeHolidaysFromWorkingDays, request.IsActive),
            cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : CreatedAtAction(nameof(GetEffective), new { asOf = result.Value!.EffectiveFrom },
                ApiResponse<PayrollCalendarPolicyDto>.Ok(result.Value!));
    }
}
