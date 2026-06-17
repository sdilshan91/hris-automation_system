using HRM.Application.Common.Security;
using HRM.Application.DTOs;
using HRM.Application.Features.TenantSettings.Commands;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Application.Features.TenantSettings.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-ADM-006: Tenant-scoped company-settings endpoints for a Tenant Admin (org profile, branding,
/// localization, security policies). All operations target ONLY the current tenant via ITenantContext (AC-5);
/// there is no tenant id in any route or body. Reads require <c>Tenant.ViewSettings</c>; writes require
/// <c>Tenant.ManageSettings</c> (BR-1 — held by Tenant Admin and Tenant Owner).
/// </summary>
[ApiController]
[Route("api/v1/tenant/settings")]
[Authorize]
public sealed class TenantSettingsController : ControllerBase
{
    // Branding upload caps mirror BrandingFileValidator; enforced again here to reject oversize bodies early.
    private const long MaxUploadBytes = BrandingFileValidator.LogoMaxBytes;

    private readonly IMediator _mediator;

    public TenantSettingsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/tenant/settings — full settings snapshot for the current tenant (FR-1).</summary>
    [HttpGet]
    [RequirePermission("Tenant.ViewSettings")]
    [ProducesResponseType(typeof(ApiResponse<TenantSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantSettingsQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TenantSettingsDto>.Ok(result.Value!));
    }

    /// <summary>PUT /api/v1/tenant/settings/org-profile — update organization profile (AC-1).</summary>
    [HttpPut("org-profile")]
    [RequirePermission("Tenant.ManageSettings")]
    [ProducesResponseType(typeof(ApiResponse<OrgProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateOrgProfile(
        [FromBody] UpdateOrgProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateOrgProfileCommand(request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OrgProfileDto>.Ok(result.Value!, "Organization profile updated."));
    }

    /// <summary>PUT /api/v1/tenant/settings/localization — update localization defaults (AC-3).</summary>
    [HttpPut("localization")]
    [RequirePermission("Tenant.ManageSettings")]
    [ProducesResponseType(typeof(ApiResponse<LocalizationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateLocalization(
        [FromBody] UpdateLocalizationRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateLocalizationCommand(request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<LocalizationDto>.Ok(result.Value!, "Localization settings updated."));
    }

    /// <summary>PUT /api/v1/tenant/settings/password-policy — update password policy (AC-4/FR-5).</summary>
    [HttpPut("password-policy")]
    [RequirePermission("Tenant.ManageSettings")]
    [ProducesResponseType(typeof(ApiResponse<PasswordPolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePasswordPolicy(
        [FromBody] UpdatePasswordPolicyRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdatePasswordPolicyCommand(request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PasswordPolicyDto>.Ok(result.Value!, "Password policy updated."));
    }

    /// <summary>PUT /api/v1/tenant/settings/session-policy — update session policy (FR-6).</summary>
    [HttpPut("session-policy")]
    [RequirePermission("Tenant.ManageSettings")]
    [ProducesResponseType(typeof(ApiResponse<SessionPolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSessionPolicy(
        [FromBody] UpdateSessionPolicyRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateSessionPolicyCommand(request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<SessionPolicyDto>.Ok(result.Value!, "Session policy updated."));
    }

    /// <summary>PUT /api/v1/tenant/settings/primary-color — update the primary brand color (FR-3).</summary>
    [HttpPut("primary-color")]
    [RequirePermission("Tenant.ManageSettings")]
    [ProducesResponseType(typeof(ApiResponse<BrandingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePrimaryColor(
        [FromBody] UpdatePrimaryColorRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdatePrimaryColorCommand(request.PrimaryColor), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<BrandingDto>.Ok(result.Value!, "Primary color updated."));
    }

    /// <summary>
    /// POST /api/v1/tenant/settings/branding/logo — upload the main logo (AC-2/FR-2/NFR-2).
    /// </summary>
    [HttpPost("branding/logo")]
    [RequirePermission("Tenant.ManageSettings")]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(typeof(ApiResponse<BrandingUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> UploadLogo(IFormFile file, CancellationToken cancellationToken)
        => UploadBrandingAsync(BrandingAssetKind.Logo, file, cancellationToken);

    /// <summary>
    /// POST /api/v1/tenant/settings/branding/email-logo — upload the email-header logo (AC-2/FR-2/NFR-2).
    /// </summary>
    [HttpPost("branding/email-logo")]
    [RequirePermission("Tenant.ManageSettings")]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(typeof(ApiResponse<BrandingUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> UploadEmailLogo(IFormFile file, CancellationToken cancellationToken)
        => UploadBrandingAsync(BrandingAssetKind.EmailLogo, file, cancellationToken);

    /// <summary>
    /// POST /api/v1/tenant/settings/branding/favicon — upload the favicon (AC-2/FR-2/NFR-2).
    /// </summary>
    [HttpPost("branding/favicon")]
    [RequirePermission("Tenant.ManageSettings")]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(typeof(ApiResponse<BrandingUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> UploadFavicon(IFormFile file, CancellationToken cancellationToken)
        => UploadBrandingAsync(BrandingAssetKind.Favicon, file, cancellationToken);

    private async Task<IActionResult> UploadBrandingAsync(
        BrandingAssetKind kind, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("No file was uploaded."));

        // Buffer the upload so the magic-byte validator can inspect the leading bytes server-side (NFR-2).
        byte[] content;
        await using (var source = file.OpenReadStream())
        await using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var result = await _mediator.Send(
            new UploadBrandingCommand(kind, content, file.FileName), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<BrandingUploadResultDto>.Ok(result.Value!, "Branding asset uploaded."));
    }
}
