using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Auth.Commands;
using HRM.Application.Features.Auth.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-AUTH-016 FR-5/AC-4: authenticated tenant-admin surface for the SSO admin-consent onboarding wizard. Kept
/// separate from <see cref="SsoController"/> (which is <c>[AllowAnonymous]</c> for the Microsoft browser
/// redirects — an action-level <c>[Authorize]</c> there would be overridden by the controller-level
/// <c>[AllowAnonymous]</c>). The Microsoft redirect-return handler lives on <see cref="SsoController"/>.
/// </summary>
[ApiController]
[Route("api/v1/tenant/sso/onboarding")]
[Authorize]
public sealed class SsoOnboardingController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public SsoOnboardingController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// POST /api/v1/tenant/sso/onboarding/admin-consent — marks onboarding <c>consent_pending</c> and returns the
    /// Microsoft admin-consent URL for the vendor multi-tenant app (AC-4). The SPA opens the URL so the customer's
    /// Microsoft 365 admin can grant tenant-wide consent. Restricted to tenant admins/owners.
    /// </summary>
    [HttpPost("admin-consent")]
    [Authorize(Roles = "Tenant Admin,Tenant Owner,System Admin")]
    [ProducesResponseType(typeof(ApiResponse<AdminConsentUrlResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartAdminConsent(CancellationToken cancellationToken)
    {
        var origin = Request.Headers.Origin.FirstOrDefault();
        var command = new BeginSsoAdminConsentCommand(_currentUser.TenantId, origin);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        }

        return Ok(ApiResponse<AdminConsentUrlResponse>.Ok(result.Value!));
    }
}
