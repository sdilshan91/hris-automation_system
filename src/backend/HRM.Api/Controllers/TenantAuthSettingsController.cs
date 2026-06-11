using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Auth.Commands;
using HRM.Application.Features.Auth.DTOs;
using HRM.Application.Features.Auth.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant authentication settings endpoints for managing MFA policy (FR-6).
/// </summary>
[ApiController]
[Route("api/v1/tenant/auth-settings")]
[Authorize]
public sealed class TenantAuthSettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public TenantAuthSettingsController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// GET /api/v1/tenant/auth-settings
    /// Returns the current tenant's MFA policy settings.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<TenantAuthSettingsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var query = new GetTenantAuthSettingsQuery(_currentUser.TenantId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));
        }

        return Ok(ApiResponse<TenantAuthSettingsResponse>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/auth-settings
    /// Updates the tenant's MFA policy. Restricted to Tenant Admin and System Admin roles.
    /// </summary>
    [HttpPut]
    [Authorize(Roles = "Tenant Admin,Tenant Owner,System Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] TenantAuthSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTenantAuthSettingsCommand(_currentUser.TenantId, request);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));
        }

        return Ok(ApiResponse.Ok("Tenant authentication settings updated."));
    }
}
