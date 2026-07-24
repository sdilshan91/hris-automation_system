using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Exposes the tenant context resolved from the request host/header before login.
/// </summary>
[ApiController]
[Route("api/v1/tenant/context")]
[AllowAnonymous]
public sealed class TenantContextController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly IAuthService _authService;

    public TenantContextController(ITenantContext tenantContext, IAuthService authService)
    {
        _tenantContext = tenantContext;
        _authService = authService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<TenantContextResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
        {
            return BadRequest(ApiResponse.Fail("Tenant context is not resolved."));
        }

        // ISSUE-204: _tenantContext.LogoUrl holds the raw internal storage path (/{tenantId}/branding/logo.png),
        // which no route serves and 404s in the browser. Expose the servable URL to the public logo endpoint
        // instead (built from this request), or null so the FE shows its fallback.
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var logoUrl = BrandingAssetUrls.Servable(
            _tenantContext.LogoUrl, baseUrl, BrandingAssetUrls.LogoRoutePath);

        // US-AUTH-016 AC-1: the pre-login page needs the SSO enforcement mode (and whether SSO is enabled) to
        // render the sso_only screen (Microsoft-only + break-glass link). Read from the cached SSO snapshot
        // (NFR-4). No secrets are exposed — only the enforcement mode + enabled flag. System context has none.
        var enforcementMode = SsoEnforcementModes.Optional;
        var ssoEnabled = false;
        if (!_tenantContext.IsSystemContext)
        {
            var sso = (await _authService.GetSsoSettingsAsync(_tenantContext.TenantId, cancellationToken)).Value;
            if (sso is not null)
            {
                enforcementMode = sso.EnforcementMode;
                ssoEnabled = sso.SsoEnabled;
            }
        }

        var response = new TenantContextResponse(
            _tenantContext.TenantId,
            _tenantContext.Subdomain,
            _tenantContext.Status,
            _tenantContext.Plan,
            _tenantContext.EnabledModules,
            _tenantContext.IsSystemContext,
            logoUrl,
            _tenantContext.PrimaryColor,
            enforcementMode,
            ssoEnabled);

        return Ok(ApiResponse<TenantContextResponse>.Ok(response));
    }
}

public sealed record TenantContextResponse(
    Guid TenantId,
    string Subdomain,
    TenantStatus Status,
    string? Plan,
    IReadOnlyCollection<string> EnabledModules,
    bool IsSystemContext,
    string? LogoUrl,
    string? PrimaryColor,
    // US-AUTH-016 AC-1: SSO sign-in enforcement for the pre-login page ("optional" | "sso_only") + enabled flag.
    string EnforcementMode,
    bool SsoEnabled);
