using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Auth.Commands;
using HRM.Application.Features.Auth.DTOs;
using HRM.Application.Features.Auth.Queries;
using HRM.Application.Features.Roles.Commands;
using HRM.Application.Features.Roles.DTOs;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped user management endpoints.
/// Provides role assignment and session management for tenant admins.
/// </summary>
[ApiController]
[Route("api/v1/tenant/users")]
[Authorize]
public sealed class TenantUsersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public TenantUsersController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
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

    #region Account Lockout Management (US-AUTH-010)

    /// <summary>
    /// POST /api/v1/tenant/users/{id}/unlock
    /// Admin unlocks a locked user account (AC-5).
    /// BR-3: Admin may only unlock users with a membership in their tenant.
    /// </summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize(Roles = "Tenant Admin,Tenant Owner")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockUser(Guid id, CancellationToken cancellationToken)
    {
        var command = new UnlockUserCommand(id, _currentUser.TenantId, _currentUser.UserId);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));
        }

        return Ok(ApiResponse.Ok("User account unlocked successfully."));
    }

    #endregion

    #region Session Management (US-AUTH-009)

    /// <summary>
    /// GET /api/v1/tenant/users/{id}/sessions
    /// Returns all active sessions for a user in the current tenant (AC-4).
    /// Restricted to Tenant Admin and Tenant Owner roles.
    /// </summary>
    [HttpGet("{id:guid}/sessions")]
    [Authorize(Roles = "Tenant Admin,Tenant Owner")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SessionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserSessions(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetUserSessionsQuery(
            id,
            _currentUser.TenantId,
            CurrentSessionId: null);

        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));
        }

        return Ok(ApiResponse<IReadOnlyList<SessionDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/users/{id}/sessions/revoke
    /// Revokes a specific session or all sessions for a user (AC-5).
    /// If body contains { sessionId }, revokes that session.
    /// If body is empty or sessionId is null, revokes all sessions.
    /// Restricted to Tenant Admin and Tenant Owner roles.
    /// </summary>
    [HttpPost("{id:guid}/sessions/revoke")]
    [Authorize(Roles = "Tenant Admin,Tenant Owner")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeUserSessions(
        Guid id,
        [FromBody] SessionRevokeRequest? request,
        CancellationToken cancellationToken)
    {
        var command = new AdminRevokeSessionsCommand(
            id,
            _currentUser.TenantId,
            request?.SessionId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));
        }

        var message = request?.SessionId.HasValue == true
            ? "Session revoked successfully."
            : "All sessions revoked successfully.";

        return Ok(ApiResponse.Ok(message));
    }

    #endregion
}
