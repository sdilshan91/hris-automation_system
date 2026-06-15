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
/// Tenant-scoped endpoints for assigning salary structures to employees and reading their compensation +
/// revision history (US-PAY-002). All operations require <c>Payroll.Configure</c> and are tenant-scoped
/// via the EF global query filter (AC-5). Inactive-structure assignment returns 400 (FR-7).
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize]
public sealed class EmployeeSalaryController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeeSalaryController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST — previews a CTC breakdown for a structure + CTC + overrides without persisting (FR-3).</summary>
    [HttpPost("salary-assignments/preview")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<CtcBreakdownDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromBody] PreviewCtcRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new PreviewCtcBreakdownQuery(request.SalaryStructureId, request.AnnualCtc, MapOverrides(request.Overrides)),
            cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<CtcBreakdownDto>.Ok(result.Value!));
    }

    /// <summary>POST — assigns a salary structure to an employee (AC-1/AC-3). 400 for an inactive structure (FR-7).</summary>
    [HttpPost("salary-assignments")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<CtcBreakdownDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Assign([FromBody] AssignSalaryStructureRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AssignSalaryStructureCommand(
            request.EmployeeId, request.SalaryStructureId, request.EffectiveFrom,
            request.AnnualCtc, request.Reason, MapOverrides(request.Overrides)), cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<CtcBreakdownDto>.Ok(result.Value!));
    }

    /// <summary>POST — bulk-assigns one structure to many employees with individual CTCs (AC-4/FR-5).</summary>
    [HttpPost("salary-assignments/bulk")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<BulkAssignResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkAssign([FromBody] BulkAssignSalaryStructureRequest request, CancellationToken cancellationToken)
    {
        var employees = request.Employees
            .Select(e => new BulkAssignEntryInput(e.EmployeeId, e.AnnualCtc, MapOverrides(e.Overrides)))
            .ToList();

        var result = await _mediator.Send(new BulkAssignSalaryStructureCommand(
            request.SalaryStructureId, request.EffectiveFrom, request.Reason, employees), cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<BulkAssignResultDto>.Ok(result.Value!));
    }

    /// <summary>GET — an employee's current compensation snapshot (FR-4 read side).</summary>
    [HttpGet("employees/{employeeId:guid}/compensation")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeCompensationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCompensation(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeCompensationQuery(employeeId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<EmployeeCompensationDto>.Ok(result.Value!));
    }

    /// <summary>GET — an employee's salary-revision history, newest first (FR-4/BR-3).</summary>
    [HttpGet("employees/{employeeId:guid}/revision-history")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SalaryRevisionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRevisions(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSalaryRevisionHistoryQuery(employeeId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<SalaryRevisionDto>>.Ok(result.Value!));
    }

    private static IReadOnlyList<SalaryOverrideInput> MapOverrides(IReadOnlyList<SalaryComponentOverrideDto> dtos)
        => dtos.Select(d => new SalaryOverrideInput(d.SalaryComponentId, d.AnnualAmount)).ToList();
}
