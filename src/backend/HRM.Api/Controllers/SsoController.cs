using HRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// CR-AUTH-001 (US-AUTH-011/013/014/015): Enterprise SSO (Microsoft Entra ID / O365) entry + callback.
///
/// <para>The "Continue with Microsoft" button hits <see cref="Challenge"/>, which redirects the browser to
/// Microsoft. Microsoft authenticates the user (incl. the customer's own MFA / conditional access) and
/// redirects back to <see cref="Callback"/> with an authorization code. The OIDC protocol — code exchange,
/// id_token validation, the fail-closed tenant-isolation guard, and user matching/JIT — lives in
/// <see cref="IEntraSsoService"/>; this controller only translates HTTP ⇆ that service and manages the
/// refresh-token cookie + browser redirects.</para>
///
/// <para>Fail-closed: when SSO is not configured, or any step fails, the user is redirected back to the
/// login page with an <c>sso_error</c> the SPA renders as a message. No app JWT is issued on any failure.</para>
/// </summary>
[ApiController]
[Route("api/v1/auth/sso")]
[AllowAnonymous]
public sealed class SsoController : ControllerBase
{
    private const string RefreshTokenCookieName = "refreshToken";

    private readonly IEntraSsoService _sso;
    private readonly IConfiguration _config;
    private readonly ILogger<SsoController> _logger;

    public SsoController(IEntraSsoService sso, IConfiguration config, ILogger<SsoController> logger)
    {
        _sso = sso;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/v1/auth/sso/challenge?tenant={subdomain}&amp;returnUrl={path} — start the Microsoft sign-in
    /// flow. A full-page browser redirect (so it cannot carry the dev X-Tenant-Subdomain header — the tenant
    /// is passed as a query param in dev / resolved from the host in prod). The SPA origin is captured from
    /// the request and carried in the signed state so the callback can return the browser to it.
    /// </summary>
    [HttpGet("challenge")]
    public async Task<IActionResult> Challenge(
        [FromQuery] string? tenant, [FromQuery] string? returnUrl, CancellationToken cancellationToken)
    {
        var spaOrigin = SpaOriginFromRequest();
        var result = await _sso.BuildAuthorizeUrlAsync(tenant, returnUrl, spaOrigin, cancellationToken);

        if (result.IsFailure)
        {
            return RedirectToLogin(spaOrigin, result.Error!);
        }

        return Redirect(result.Value!);
    }

    /// <summary>
    /// GET /api/v1/auth/sso/callback — the OIDC redirect URI. Completes the flow and, on success, sets the
    /// refresh-token cookie and redirects to the SPA's SSO landing route (no tokens in the URL — the SPA
    /// mints its access token via the existing refresh flow).
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        var result = await _sso.CompleteSignInAsync(
            code, state, error, errorDescription, GetIpAddress(), GetUserAgent(), cancellationToken);

        if (result.IsFailure)
        {
            return RedirectToLogin(FrontendBaseUrl(), result.Error!);
        }

        var signIn = result.Value!;

        // Hand the session back the same way local login does: refresh token in an httpOnly cookie; the SPA
        // calls /auth/refresh to mint its in-memory access token. Never put tokens in the redirect URL.
        if (!string.IsNullOrEmpty(signIn.Login.RefreshToken))
        {
            SetRefreshTokenCookie(signIn.Login.RefreshToken);
        }

        var origin = string.IsNullOrEmpty(signIn.ReturnOrigin) ? FrontendBaseUrl() : signIn.ReturnOrigin;
        var returnUrl = string.IsNullOrEmpty(signIn.ReturnUrl) ? "/dashboard" : signIn.ReturnUrl;
        return Redirect($"{origin}/auth/sso/callback?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    // ── US-AUTH-016: admin-consent onboarding ─────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/auth/sso/admin-consent/callback — US-AUTH-016 FR-6/AC-5/AC-6. The Microsoft admin-consent
    /// redirect URI. On success (<c>admin_consent=True</c> + <c>tenant</c>=customer tid) captures the directory id
    /// into the tenant allow-list and advances onboarding; on decline/error records the failure and keeps the
    /// prior mode. Full-page browser redirect (anonymous) — the HRM tenant comes from the signed state.
    /// </summary>
    [HttpGet("admin-consent/callback")]
    public async Task<IActionResult> AdminConsentCallback(
        [FromQuery(Name = "admin_consent")] string? adminConsent,
        [FromQuery] string? tenant,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        var result = await _sso.CompleteAdminConsentAsync(
            tenant, adminConsent, state, error, errorDescription, GetIpAddress(), GetUserAgent(), cancellationToken);

        if (result.IsFailure)
        {
            // Unparseable state — we can't safely attribute the outcome to a tenant. Send to a generic error page.
            return Redirect($"{FrontendBaseUrl()}/settings/security/sso?consent=error");
        }

        var outcome = result.Value!;
        var origin = string.IsNullOrEmpty(outcome.ReturnOrigin) ? FrontendBaseUrl() : outcome.ReturnOrigin;
        var status = outcome.Succeeded ? "success" : "failed";
        return Redirect($"{origin}/settings/security/sso?consent={status}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private void SetRefreshTokenCookie(string token)
    {
        Response.Cookies.Append(RefreshTokenCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/api/v1/auth",
        });
    }

    /// <summary>The SPA origin that initiated the challenge (from Origin/Referer), for the return redirect.</summary>
    private string? SpaOriginFromRequest()
    {
        var origin = Request.Headers.Origin.FirstOrDefault();
        if (!string.IsNullOrEmpty(origin))
        {
            return origin;
        }

        var referer = Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
        {
            return refUri.GetLeftPart(UriPartial.Authority);
        }

        return null;
    }

    /// <summary>Configured SPA base URL — used for callback redirects, where Origin/Referer point at
    /// Microsoft and cannot be trusted.</summary>
    private string FrontendBaseUrl() =>
        _config["Platform:FrontendBaseUrl"] ?? "http://localhost:4200";

    private IActionResult RedirectToLogin(string? origin, string ssoErrorCode)
    {
        var baseUrl = string.IsNullOrEmpty(origin) ? FrontendBaseUrl() : origin;
        return Redirect($"{baseUrl}/auth/login?sso_error={Uri.EscapeDataString(ssoErrorCode)}");
    }

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() => Request.Headers.UserAgent.FirstOrDefault();
}
