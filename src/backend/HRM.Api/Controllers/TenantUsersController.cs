using HRM.Application.DTOs;
using HRM.Application.Features.Roles.Commands;
using HRM.Application.Features.Roles.DTOs;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped user management endpoints.
/// Currently provides role assignment; will expand with Core HR module.
/// </summary>
[ApiController]
[Route("api/v1/tenant/users")]
[Authorize]
public sealed class TenantUsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public TenantUsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// PATCH /api/v1/tenant/users/{id}
    /// Assigns roles to a user's tenant membership.
    /// AC-3: Updates user_tenant_role; next JWT refresh carries updated claims.
    /// FR-4, FR-8: Replaces the current role set; protects sole Tenant Owner.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("Roles.AssignUsers")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignUserRoles(Guid id, [FromBody] AssignUserRolesRequest request, CancellationToken cancellationToken)
    {
        var command = new AssignUserRolesCommand(id, request.RoleIds);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("User roles updated successfully."));
    }
}
