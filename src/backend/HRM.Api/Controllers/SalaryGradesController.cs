using HRM.Application.DTOs;
using HRM.Application.Features.SalaryGrades.Commands;
using HRM.Application.Features.SalaryGrades.DTOs;
using HRM.Application.Features.SalaryGrades.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped salary grade (pay band) management endpoints (Payroll domain, ISSUE-021).
/// Reads require Payroll.View; writes require Payroll.Configure.
/// </summary>
[ApiController]
[Route("api/v1/tenant/salary-grades")]
[Authorize]
public sealed class SalaryGradesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SalaryGradesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/salary-grades
    /// Lists salary grades for the current tenant, ordered by code.
    /// Optional query parameter: includeInactive (bool).
    /// </summary>
    [HttpGet]
    [RequirePermission("Payroll.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SalaryGradeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSalaryGrades(
        [FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSalaryGradesQuery(includeInactive), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<SalaryGradeDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/salary-grades/{id}
    /// Gets a single salary grade by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Payroll.View")]
    [ProducesResponseType(typeof(ApiResponse<SalaryGradeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSalaryGradeById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSalaryGradeByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<SalaryGradeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/salary-grades
    /// Creates a new salary grade.
    /// </summary>
    [HttpPost]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryGradeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateSalaryGrade(
        [FromBody] CreateSalaryGradeRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateSalaryGradeCommand(
            request.Code, request.Name, request.MinAmount, request.MidAmount,
            request.MaxAmount, request.Currency, request.Description);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetSalaryGradeById),
            new { id = result.Value!.Id },
            ApiResponse<SalaryGradeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/salary-grades/{id}
    /// Updates an existing salary grade.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<SalaryGradeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateSalaryGrade(
        Guid id, [FromBody] UpdateSalaryGradeRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateSalaryGradeCommand(
            id, request.Code, request.Name, request.MinAmount, request.MidAmount,
            request.MaxAmount, request.Currency, request.Description);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<SalaryGradeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// DELETE /api/v1/tenant/salary-grades/{id}
    /// Deactivates (soft-delete) a salary grade.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSalaryGrade(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateSalaryGradeCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Salary grade deactivated successfully."));
    }
}
