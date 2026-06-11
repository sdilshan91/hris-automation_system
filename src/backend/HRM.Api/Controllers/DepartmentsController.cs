using HRM.Application.DTOs;
using HRM.Application.Features.Departments.Commands;
using HRM.Application.Features.Departments.DTOs;
using HRM.Application.Features.Departments.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped department management endpoints (US-CHR-004).
/// Requires Department.View or Department.Create/Edit/Deactivate permissions.
/// </summary>
[ApiController]
[Route("api/v1/tenant/departments")]
[Authorize]
public sealed class DepartmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DepartmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/departments
    /// Lists all departments for the current tenant (flat list).
    /// Optional query parameter: activeOnly (bool).
    /// </summary>
    [HttpGet]
    [RequirePermission("Department.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DepartmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDepartments(
        [FromQuery] bool? activeOnly, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDepartmentsQuery(activeOnly), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<DepartmentDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/departments/tree
    /// Returns the department hierarchy as a tree structure (FR-8).
    /// </summary>
    [HttpGet("tree")]
    [RequirePermission("Department.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DepartmentTreeNodeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDepartmentTree(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDepartmentTreeQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<DepartmentTreeNodeDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/departments/{id}
    /// Gets a single department by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Department.View")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDepartmentById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDepartmentByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<DepartmentDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/departments
    /// Creates a new department (AC-2).
    /// </summary>
    [HttpPost]
    [RequirePermission("Department.Create")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDepartment(
        [FromBody] CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateDepartmentCommand(
            request.Name, request.Code, request.Description,
            request.ParentDepartmentId, request.ManagerId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetDepartmentById),
            new { id = result.Value!.Id },
            ApiResponse<DepartmentDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/departments/{id}
    /// Updates an existing department (AC-4).
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Department.Edit")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDepartment(
        Guid id, [FromBody] UpdateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateDepartmentCommand(
            id, request.Name, request.Code, request.Description,
            request.ParentDepartmentId, request.ManagerId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<DepartmentDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/departments/{id}/deactivate
    /// Deactivates (soft-deletes) a department (AC-5, FR-6, FR-7).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("Department.Deactivate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateDepartment(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateDepartmentCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Department deactivated successfully."));
    }
}
