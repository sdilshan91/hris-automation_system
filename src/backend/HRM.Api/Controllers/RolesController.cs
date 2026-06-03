using HRM.Application.DTOs;
using HRM.Application.Features.Roles.Commands;
using HRM.Application.Features.Roles.DTOs;
using HRM.Application.Features.Roles.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped role management endpoints (FR-6).
/// Requires Roles.View or Roles.Manage permissions.
/// </summary>
[ApiController]
[Route("api/v1/tenant/roles")]
[Authorize]
public sealed class RolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RolesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/roles
    /// Lists all roles (built-in + custom) for the current tenant.
    /// AC-1: Displays built-in roles (non-editable) and custom roles with permission/user counts.
    /// </summary>
    [HttpGet]
    [RequirePermission("Roles.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RoleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetRolesQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<RoleDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/roles/{id}
    /// Gets a single role with its permissions and user count.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Roles.View")]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRoleById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetRoleByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<RoleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/roles
    /// Creates a new custom role scoped to the current tenant.
    /// AC-2: Creates role with name, description, and permission list.
    /// </summary>
    [HttpPost]
    [RequirePermission("Roles.Manage")]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateRoleCommand(request.Name, request.Description, request.Permissions);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetRoleById),
            new { id = result.Value!.Id },
            ApiResponse<RoleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/roles/{id}
    /// Updates a custom role's name, description, and permissions.
    /// Built-in roles return 400.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Roles.Manage")]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateRoleCommand(id, request.Name, request.Description, request.Permissions);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<RoleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// DELETE /api/v1/tenant/roles/{id}
    /// Deletes a custom role. Built-in roles cannot be deleted (AC-6).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Roles.Manage")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteRoleCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Role deleted successfully."));
    }

    /// <summary>
    /// GET /api/v1/tenant/roles/permissions
    /// Returns the full permission catalog grouped by module.
    /// </summary>
    [HttpGet("permissions")]
    [RequirePermission("Roles.View")]
    [ProducesResponseType(typeof(ApiResponse<PermissionCatalogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissionCatalog(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPermissionCatalogQuery(), cancellationToken);
        return Ok(ApiResponse<PermissionCatalogDto>.Ok(result.Value!));
    }
}
