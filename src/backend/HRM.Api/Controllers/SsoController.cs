using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-AUTH-011 / US-AUTH-015: Enterprise SSO (Microsoft Entra ID / O365) entry point.
///
/// <para><b>Increment 1 (this code):</b> the "Continue with Microsoft" button on the login page hits
/// <see cref="Challenge"/>. It is CONFIG-DRIVEN and FAIL-CLOSED — when no vendor Entra app registration
/// is configured (<c>Authentication:Entra:Enabled=false</c> or a blank <c>ClientId</c>) the user is
/// redirected back to the login page with a clear "not configured" message. No insecure or half-wired
/// OIDC flow is exposed.</para>
///
/// <para><b>Increment 2 (requires the Azure app registration — see CR-AUTH-001):</b> the secure OIDC
/// Authorization-Code round-trip — building the authorize redirect with a signed <c>state</c>, the
/// <c>/callback</c> handler, <c>id_token</c> validation, the US-AUTH-013 <c>tid</c>/domain isolation
/// guard (fail-closed), and US-AUTH-014 user matching that mints the app JWT. Until that ships (and a
/// real Entra app exists to test against), the configured path reports "not available".</para>
/// </summary>
[ApiController]
[Route("api/v1/auth/sso")]
[AllowAnonymous]
public sealed class SsoController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<SsoController> _logger;

    public SsoController(IConfiguration config, ILogger<SsoController> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/v1/auth/sso/challenge?tenant={subdomain}&amp;returnUrl={path} — start the Microsoft
    /// sign-in flow. A full-page browser redirect (so it cannot carry the dev X-Tenant-Subdomain
    /// header — the tenant is passed as a query param in dev / resolved from the host in prod).
    /// </summary>
    [HttpGet("challenge")]
    public IActionResult Challenge([FromQuery] string? tenant, [FromQuery] string? returnUrl)
    {
        var entra = _config.GetSection("Authentication:Entra");
        var configured = entra.GetValue<bool>("Enabled")
                         && !string.IsNullOrWhiteSpace(entra["ClientId"]);

        if (!configured)
        {
            _logger.LogInformation(
                "SSO challenge for tenant {Tenant} rejected: Entra app registration not configured.", tenant);
            return RedirectToLogin("sso_error=not_configured");
        }

        // Entra IS configured, but the secure round-trip (authorize redirect + /callback + id_token
        // validation + US-AUTH-013 tid/domain isolation + US-AUTH-014 matching) is Increment 2 and
        // must not be enabled until it exists. Fail closed with a clear message rather than sending the
        // user to Microsoft with no handler to safely return to.
        _logger.LogInformation(
            "SSO challenge for tenant {Tenant}: Entra configured but the OIDC round-trip (Increment 2) is not yet implemented.", tenant);
        return RedirectToLogin("sso_error=not_available");
    }

    /// <summary>
    /// GET /api/v1/auth/sso/callback — the OIDC redirect URI. Implemented in Increment 2
    /// (US-AUTH-011 callback + US-AUTH-013 isolation + US-AUTH-014 matching); see CR-AUTH-001.
    /// </summary>
    [HttpGet("callback")]
    public IActionResult Callback() => RedirectToLogin("sso_error=not_available");

    /// <summary>
    /// Redirect back to the login page on the SAME origin the browser came from (the tenant subdomain
    /// in prod, localhost:4200 in dev), falling back to a configured base if Origin/Referer are absent.
    /// </summary>
    private IActionResult RedirectToLogin(string query)
    {
        var origin = Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin))
        {
            var referer = Request.Headers.Referer.FirstOrDefault();
            if (!string.IsNullOrEmpty(referer)
                && Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
            {
                origin = refUri.GetLeftPart(UriPartial.Authority);
            }
        }

        origin ??= _config["Platform:FrontendBaseUrl"] ?? "http://localhost:4200";
        return Redirect($"{origin}/auth/login?{query}");
    }
}
