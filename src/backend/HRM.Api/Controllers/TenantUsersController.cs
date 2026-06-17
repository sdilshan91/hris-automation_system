using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Auth.Commands;
using HRM.Application.Features.Auth.DTOs;
using HRM.Application.Features.Auth.Queries;
using HRM.Application.Features.Roles.Commands;
using HRM.Application.Features.Roles.DTOs;
using HRM.Application.Features.Users.Commands;
using HRM.Application.Features.Users.DTOs;
using HRM.Application.Features.Users.Queries;
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

    #region Tenant User Management (US-ADM-005)

    /// <summary>
    /// GET /api/v1/tenant/users
    /// AC-1/FR-1: paginated, searchable, filterable list of users in the current tenant.
    /// </summary>
    [HttpGet]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TenantUserListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? roleId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListTenantUsersQuery(page, pageSize, search, status, roleId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PagedResult<TenantUserListItemDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/users/{id}/detail
    /// FR-6: profile + roles + active sessions + invitation history for one membership.
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse<TenantUserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserDetail(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantUserDetailQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TenantUserDetailDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/users/invite
    /// AC-2/FR-2: invite a single user by email + role ids.
    /// </summary>
    [HttpPost("invite")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse<InviteResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new InviteUserCommand(request.Email, request.RoleIds), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<InviteResultDto>.Ok(result.Value!, "Invitation sent."));
    }

    /// <summary>
    /// POST /api/v1/tenant/users/invite/bulk
    /// FR-3: bulk/CSV invite — an array of {email, roleNames}. Valid rows succeed; invalid rows return per-row errors.
    /// </summary>
    [HttpPost("invite/bulk")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse<InviteResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkInvite([FromBody] BulkInviteRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new BulkInviteUsersCommand(request.Rows), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<InviteResultDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/users/{id}/roles
    /// AC-3/FR-4: replace a membership's role set with a new complete set. BR-2: Tenant Owner cannot be removed.
    /// </summary>
    [HttpPut("{id:guid}/roles")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditRoles(Guid id, [FromBody] EditUserRolesRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new EditUserRolesCommand(id, request.RoleIds), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse.Ok("User roles updated successfully."));
    }

    /// <summary>
    /// POST /api/v1/tenant/users/{id}/deactivate
    /// AC-4: status→Disabled, revoke this-tenant sessions, audit. BR-3: cannot deactivate yourself.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateUserCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse.Ok("User deactivated."));
    }

    /// <summary>
    /// POST /api/v1/tenant/users/{id}/force-password-reset
    /// AC-5: revoke ALL refresh tokens across all tenants (global password), null PasswordChangedAt, email, audit.
    /// </summary>
    [HttpPost("{id:guid}/force-password-reset")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForcePasswordReset(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ForcePasswordResetCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse.Ok("Password reset forced; the user must set a new password."));
    }

    /// <summary>
    /// POST /api/v1/tenant/users/{id}/end-sessions
    /// FR-5: revoke all of the user's refresh tokens within the current tenant only, audit.
    /// </summary>
    [HttpPost("{id:guid}/end-sessions")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndSessions(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new EndAllSessionsCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse.Ok("All sessions ended for this tenant."));
    }

    /// <summary>
    /// GET /api/v1/tenant/users/invitations
    /// AC-2: the Pending Invitations tab (all invitations for the current tenant).
    /// </summary>
    [HttpGet("invitations")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<InvitationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvitations(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListInvitationsQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<InvitationDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/users/invitations/{id}/resend
    /// BR-6: resend an invitation with a NEW one-time token + fresh 72h expiry.
    /// </summary>
    [HttpPost("invitations/{id:guid}/resend")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse<InvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ResendInvitationCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<InvitationDto>.Ok(result.Value!, "Invitation resent."));
    }

    /// <summary>
    /// POST /api/v1/tenant/users/invitations/{id}/revoke
    /// AC-2: revoke a pending invitation (status → Revoked).
    /// </summary>
    [HttpPost("invitations/{id:guid}/revoke")]
    [RequirePermission("Tenant.ManageUsers")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RevokeInvitationCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse.Ok("Invitation revoked."));
    }

    #endregion

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
