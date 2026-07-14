using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Enums;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped endpoints for configuring statutory deduction rules — income-tax slabs, EPF/ETF and other
/// social-security contributions (US-PAY-006). All operations require <c>Payroll.Configure</c> (the catalog
/// permission HR Officer / Tenant Admin already hold, matching the story's persona). Every query is
/// tenant-scoped via the EF global query filter (AC-4). The test-calculation endpoint (FR-5) is
/// side-effect-free.
/// </summary>
[ApiController]
[Route("api/v1/payroll/statutory-rules")]
[Authorize]
public sealed class StatutoryRulesController : ControllerBase
{
    private readonly IMediator _mediator;

    public StatutoryRulesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET — lists statutory rules, paged + filterable by type / fiscal year / active (default 25 / max 100).</summary>
    [HttpGet]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<StatutoryRuleListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] StatutoryRuleType? ruleType,
        [FromQuery] string? fiscalYear,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListStatutoryRulesQuery(ruleType, fiscalYear, isActive, page, pageSize), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PagedResult<StatutoryRuleListItemDto>>.Ok(result.Value!));
    }

    /// <summary>GET — distinct fiscal years that have statutory rules, newest-first (FR-4, FY selector).</summary>
    [HttpGet("fiscal-years")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFiscalYears(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListStatutoryFiscalYearsQuery(), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<string>>.Ok(result.Value!));
    }

    /// <summary>GET — single statutory rule by id (tenant-scoped).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<StatutoryRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStatutoryRuleByIdQuery(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<StatutoryRuleDto>.Ok(result.Value!));
    }

    /// <summary>POST — creates a statutory rule with its slabs / social-security parameters (AC-1/AC-2).</summary>
    [HttpPost]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<StatutoryRuleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateStatutoryRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateStatutoryRuleCommand(
            request.RuleType, request.RuleName, request.CountryCode, request.FiscalYear,
            request.EffectiveFrom, request.EffectiveTo, request.IsActive,
            request.TaxSlabs.Select(s => new TaxSlabInput(s.SlabFrom, s.SlabTo, s.RatePercentage, s.OrderIndex)).ToList(),
            request.SocialSecurity is null ? null : new SocialSecurityInputDto(
                request.SocialSecurity.EmployeeRate, request.SocialSecurity.EmployerRate,
                request.SocialSecurity.WageCeilingAnnual, request.SocialSecurity.ApplicableOn,
                request.SocialSecurity.ApplicableComponentIds),
            request.Exemptions.Select(e => new ExemptionInput(
                e.Name, e.CalculationType, e.Value, e.ComponentId, e.MaxAmount, e.IsAnnual, e.OrderIndex)).ToList(),
            request.IsCumulative),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ApiResponse<StatutoryRuleDto>.Ok(result.Value!));
    }

    /// <summary>PUT — updates a statutory rule's slabs / social-security parameters (AC-1).</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<StatutoryRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStatutoryRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateStatutoryRuleCommand(
            id, request.RuleName, request.CountryCode, request.FiscalYear,
            request.EffectiveFrom, request.EffectiveTo, request.IsActive,
            request.TaxSlabs.Select(s => new TaxSlabInput(s.SlabFrom, s.SlabTo, s.RatePercentage, s.OrderIndex)).ToList(),
            request.SocialSecurity is null ? null : new SocialSecurityInputDto(
                request.SocialSecurity.EmployeeRate, request.SocialSecurity.EmployerRate,
                request.SocialSecurity.WageCeilingAnnual, request.SocialSecurity.ApplicableOn,
                request.SocialSecurity.ApplicableComponentIds),
            request.Exemptions.Select(e => new ExemptionInput(
                e.Name, e.CalculationType, e.Value, e.ComponentId, e.MaxAmount, e.IsAnnual, e.OrderIndex)).ToList(),
            request.IsCumulative),
            cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<StatutoryRuleDto>.Ok(result.Value!));
    }

    /// <summary>DELETE — soft-deletes a statutory rule.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteStatutoryRuleCommand(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse.Ok("Statutory rule deleted."));
    }

    /// <summary>POST — clones one fiscal year's statutory rules into a new fiscal year (AC-3).</summary>
    [HttpPost("clone-fiscal-year")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StatutoryRuleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CloneFiscalYear([FromBody] CloneFiscalYearRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CloneFiscalYearCommand(
            request.SourceFiscalYear, request.TargetFiscalYear, request.EffectiveFrom, request.EffectiveTo), cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<StatutoryRuleDto>>.Ok(result.Value!));
    }

    /// <summary>POST — test calculation (FR-5): computes statutory deductions for a sample salary, no persistence.</summary>
    [HttpPost("test-calculation")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<StatutoryCalculationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestCalculation([FromBody] TestCalculationRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new TestStatutoryCalculationQuery(
            request.MonthlyGross, request.MonthlyBasic, request.DeclaredMonthlyExemptions,
            request.ExemptEarnings, request.FiscalYear, request.CountryCode,
            request.ComponentAmounts, request.PriorTaxableIncomeYtd, request.PriorTaxWithheldYtd), cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<StatutoryCalculationResultDto>.Ok(result.Value!));
    }
}
