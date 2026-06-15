using HRM.Application.Common.Interfaces;
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
/// Tenant-scoped endpoints for managing salary structures and their component links (US-PAY-001). All
/// operations require <c>Payroll.Configure</c>. All queries are tenant-scoped via the EF global query
/// filter (AC-6).
/// </summary>
[ApiController]
[Route("api/v1/payroll/salary-structures")]
[Authorize]
public sealed class SalaryStructuresController : ControllerBase
{
    private readonly IMediator _mediator;

    public SalaryStructuresController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET — lists salary structures, paged + filterable (NFR-3, default 25 / max 100).</summary>
    [HttpGet]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryStructurePageResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ListSalaryStructuresQuery(isActive, search, page, pageSize), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var paged = result.Value!;
        return Ok(ApiResponse<SalaryStructurePageResponse>.Ok(new SalaryStructurePageResponse
        {
            Data = paged.Items,
            Total = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        }));
    }

    /// <summary>GET — single salary structure (with components) by id (tenant-scoped).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryStructureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSalaryStructureByIdQuery(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<SalaryStructureDto>.Ok(result.Value!));
    }

    /// <summary>POST — creates a salary structure with its component links (AC-3).</summary>
    [HttpPost]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryStructureDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSalaryStructureRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateSalaryStructureCommand(
            request.Name, request.Code, request.Description, request.EffectiveFrom,
            request.IsDefault, request.IsActive, MapInputs(request.Components)), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ApiResponse<SalaryStructureDto>.Ok(result.Value!));
    }

    /// <summary>PUT — updates a salary structure and replaces its component links.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryStructureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSalaryStructureRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateSalaryStructureCommand(
            id, request.Name, request.Code, request.Description, request.EffectiveFrom,
            request.IsDefault, request.IsActive, MapInputs(request.Components)), cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<SalaryStructureDto>.Ok(result.Value!));
    }

    /// <summary>DELETE — soft-deletes a structure (and its links).</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteSalaryStructureCommand(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse.Ok("Salary structure deleted."));
    }

    /// <summary>POST — links a single component to a structure (FR-3).</summary>
    [HttpPost("{id:guid}/components")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryStructureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkComponent(Guid id, [FromBody] LinkComponentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new LinkComponentToStructureCommand(
            id, request.SalaryComponentId, request.OverrideValue, request.OverrideFormula,
            request.ProcessingOrder, request.IsMandatory), cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<SalaryStructureDto>.Ok(result.Value!));
    }

    /// <summary>DELETE — removes a component link from a structure (FR-3). 409 for mandatory statutory links (BR-3).</summary>
    [HttpDelete("{id:guid}/components/{structureComponentId:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryStructureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkComponent(Guid id, Guid structureComponentId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UnlinkComponentFromStructureCommand(id, structureComponentId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<SalaryStructureDto>.Ok(result.Value!));
    }

    /// <summary>POST — reorders component processing priority within a structure (AC-4).</summary>
    [HttpPost("{id:guid}/components/reorder")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryStructureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(Guid id, [FromBody] ReorderComponentsRequest request, CancellationToken cancellationToken)
    {
        var order = request.Order.Select(e => (e.SalaryStructureComponentId, e.ProcessingOrder)).ToList();
        var result = await _mediator.Send(new ReorderStructureComponentsCommand(id, order), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<SalaryStructureDto>.Ok(result.Value!));
    }

    /// <summary>POST — clones a structure into a new one (FR-6).</summary>
    [HttpPost("{id:guid}/clone")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryStructureDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Clone(Guid id, [FromBody] CloneSalaryStructureRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CloneSalaryStructureCommand(id, request.Name, request.Code), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ApiResponse<SalaryStructureDto>.Ok(result.Value!));
    }

    private static IReadOnlyList<SalaryStructureComponentInput> MapInputs(IReadOnlyList<SalaryStructureComponentInputDto> dtos)
        => dtos.Select(d => new SalaryStructureComponentInput(
            d.SalaryComponentId, d.OverrideValue, d.OverrideFormula, d.ProcessingOrder, d.IsMandatory)).ToList();
}
