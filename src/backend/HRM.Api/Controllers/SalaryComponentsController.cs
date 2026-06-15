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
/// Tenant-scoped endpoints for managing salary components (US-PAY-001). All operations require
/// <c>Payroll.Configure</c> (the catalog permission HR Officer / Tenant Admin already hold — matching
/// the story's Tenant Admin / HR Officer persona). All queries are tenant-scoped via the EF global
/// query filter (AC-6).
/// </summary>
[ApiController]
[Route("api/v1/payroll/salary-components")]
[Authorize]
public sealed class SalaryComponentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SalaryComponentsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET — lists salary components, paged + filterable (NFR-3, default 25 / max 100).</summary>
    [HttpGet]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryComponentPageResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] SalaryComponentType? type,
        [FromQuery] bool? isActive,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListSalaryComponentsQuery(type, isActive, search, page, pageSize), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var paged = result.Value!;
        return Ok(ApiResponse<SalaryComponentPageResponse>.Ok(new SalaryComponentPageResponse
        {
            Data = paged.Items,
            Total = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        }));
    }

    /// <summary>GET — single salary component by id (tenant-scoped).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryComponentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSalaryComponentByIdQuery(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<SalaryComponentDto>.Ok(result.Value!));
    }

    /// <summary>POST — creates a salary component (AC-1).</summary>
    [HttpPost]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryComponentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSalaryComponentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateSalaryComponentCommand(
            request.Name, request.Code, request.Type, request.CalculationMethod, request.DefaultValue,
            request.FormulaExpression, request.IsTaxable, request.IsStatutory, request.IsActive, request.ProcessingOrder),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ApiResponse<SalaryComponentDto>.Ok(result.Value!));
    }

    /// <summary>PUT — updates a salary component (AC-2).</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryComponentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSalaryComponentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateSalaryComponentCommand(
            id, request.Name, request.Code, request.Type, request.CalculationMethod, request.DefaultValue,
            request.FormulaExpression, request.IsTaxable, request.IsStatutory, request.IsActive, request.ProcessingOrder),
            cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<SalaryComponentDto>.Ok(result.Value!));
    }

    /// <summary>DELETE — soft-deletes a component; 409 when it is used by any structure (AC-5).</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteSalaryComponentCommand(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse.Ok("Salary component deleted."));
    }
}
