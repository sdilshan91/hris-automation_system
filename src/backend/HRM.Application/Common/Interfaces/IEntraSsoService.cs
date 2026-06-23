using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// CR-AUTH-001 Increment 2 — the Microsoft Entra (O365) OIDC protocol layer. Owns the Authorization-Code
/// round-trip: building the signed authorize redirect, exchanging the code, validating the id_token
/// (signature/issuer/audience/nonce), and enforcing the US-AUTH-013 tenant-isolation guard (fail-closed).
/// On success it terminates in the application's own JWT via <see cref="IAuthService"/> — SSO is purely a
/// new front door onto the existing session model.
/// </summary>
public interface IEntraSsoService
{
    /// <summary>True only when Entra SSO can actually complete a round-trip (enabled + client id + secret
    /// + redirect uri present). When false the login button must report "not configured".</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Builds the Microsoft authorize URL for the "Continue with Microsoft" redirect. Generates PKCE +
    /// nonce and packs them — with the tenant subdomain, return path and SPA origin — into a tamper-evident,
    /// time-limited signed <c>state</c>. Fails (caller shows "not configured") when SSO is not configured.
    /// </summary>
    /// <param name="subdomain">Resolved HRM tenant subdomain (dev: from <c>?tenant=</c>).</param>
    /// <param name="returnUrl">Post-login SPA path (e.g. <c>/dashboard</c>).</param>
    /// <param name="returnOrigin">SPA origin to return the browser to (from the challenge request).</param>
    Task<Result<string>> BuildAuthorizeUrlAsync(
        string? subdomain, string? returnUrl, string? returnOrigin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles the Microsoft redirect back (<c>/signin</c> callback): validates state, exchanges the code,
    /// validates the id_token, enforces the tenant-isolation guard, then matches/links/provisions the HRM
    /// user and issues the application JWT (+ refresh). Returns the login payload and where to send the
    /// browser. Every failure path is fail-closed and maps to an <c>sso_error</c> code for the login page.
    /// </summary>
    Task<Result<SsoSignInResult>> CompleteSignInAsync(
        string? code, string? state, string? error, string? errorDescription,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a successful SSO callback: the freshly-minted session plus where to redirect the SPA.</summary>
public sealed record SsoSignInResult
{
    public required LoginResponse Login { get; init; }

    /// <summary>SPA origin to redirect to (e.g. <c>http://localhost:4200</c>).</summary>
    public required string ReturnOrigin { get; init; }

    /// <summary>Post-login SPA path (e.g. <c>/dashboard</c>).</summary>
    public required string ReturnUrl { get; init; }
}
