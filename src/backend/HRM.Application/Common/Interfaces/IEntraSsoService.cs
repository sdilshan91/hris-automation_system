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

    /// <summary>True when the admin-consent flow can be started (enabled + client id + admin-consent redirect uri
    /// present). The client secret is NOT required to BUILD the consent URL, so this is a weaker gate than
    /// <see cref="IsConfigured"/>.</summary>
    bool IsAdminConsentConfigured { get; }

    /// <summary>
    /// US-AUTH-016 FR-5/AC-4: builds the Microsoft admin-consent URL for the vendor multi-tenant app so the
    /// customer's Microsoft 365 admin can grant tenant-wide consent. Uses the vendor <c>client_id</c>, the
    /// <c>organizations</c> authority and a FIXED admin-consent redirect. Packs the HRM tenant subdomain (so the
    /// callback can resolve the tenant — never trusting the token/consent <c>tid</c> for that) and the SPA origin
    /// into a tamper-evident, time-limited signed <c>state</c>. Fails when admin consent is not configured.
    /// </summary>
    Task<Result<string>> BuildAdminConsentUrlAsync(
        string? subdomain, string? returnOrigin, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-AUTH-016 FR-6/AC-5/AC-6: handles the Microsoft admin-consent redirect back. Validates the signed state
    /// (→ HRM subdomain + return origin). On success (<paramref name="adminConsent"/> = "True", non-empty
    /// <paramref name="tenant"/>) it captures the customer directory id via
    /// <see cref="IAuthService.CaptureAdminConsentAsync"/>; on decline/error it records the failure via
    /// <see cref="IAuthService.RecordAdminConsentFailureAsync"/> and leaves the prior mode intact. Returns where to
    /// send the browser plus whether consent succeeded; a Failure result means the state itself was unparseable.
    /// </summary>
    Task<Result<AdminConsentResult>> CompleteAdminConsentAsync(
        string? tenant, string? adminConsent, string? state, string? error, string? errorDescription,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of the admin-consent callback: where to send the SPA and whether consent was granted.</summary>
public sealed record AdminConsentResult
{
    /// <summary>SPA origin to redirect to (e.g. <c>http://localhost:4200</c>). Empty ⇒ caller uses its default.</summary>
    public required string ReturnOrigin { get; init; }

    /// <summary>True when the customer admin granted consent and the directory id was captured (AC-5).</summary>
    public required bool Succeeded { get; init; }
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
