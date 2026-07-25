using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// CR-AUTH-001 Increment 2 — Microsoft Entra (O365) OIDC Authorization-Code flow, implemented manually so it
/// terminates in the application's own JWT (see <see cref="IAuthService.SsoSignInAsync"/>) rather than the
/// opinionated cookie middleware. Crypto-sensitive parts use vetted libraries: ASP.NET Data Protection for the
/// tamper-evident, time-limited <c>state</c>, and Microsoft.IdentityModel for id_token signature/issuer
/// validation against the discovery document's JWKS. PKCE (S256) and nonce protect the code exchange.
/// </summary>
public sealed class EntraSsoService : IEntraSsoService
{
    private const int StateLifetimeMinutes = 10;
    private const string HttpClientName = "EntraSso";

    private readonly EntraSsoOptions _options;
    private readonly ITimeLimitedDataProtector _stateProtector;
    private readonly ITimeLimitedDataProtector _adminConsentStateProtector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _oidcConfig;
    private readonly IAuthService _authService;
    private readonly ILogger<EntraSsoService> _logger;

    public EntraSsoService(
        IOptions<EntraSsoOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        ConfigurationManager<OpenIdConnectConfiguration> oidcConfig,
        IAuthService authService,
        ILogger<EntraSsoService> logger)
    {
        _options = options.Value;
        _stateProtector = dataProtectionProvider.CreateProtector("HRM.EntraSso.State").ToTimeLimitedDataProtector();
        // US-AUTH-016: a distinct purpose so a login-flow state can never be replayed as an admin-consent state.
        _adminConsentStateProtector = dataProtectionProvider.CreateProtector("HRM.EntraSso.AdminConsent").ToTimeLimitedDataProtector();
        _httpClientFactory = httpClientFactory;
        _oidcConfig = oidcConfig;
        _authService = authService;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public bool IsAdminConsentConfigured => _options.IsAdminConsentConfigured;

    public async Task<Result<string>> BuildAuthorizeUrlAsync(
        string? subdomain, string? returnUrl, string? returnOrigin, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result<string>.Failure("not_configured");
        }

        var tenant = (subdomain ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(tenant))
        {
            // ISSUE-220: distinguish "no/unresolved tenant supplied" from "SSO genuinely not configured".
            // Reusing `not_configured` here wrongly implied SSO was disabled for a request that simply carried
            // no tenant; `tenant_required` lets a genuinely-misconfigured deployment be told apart in logs/UX.
            _logger.LogInformation("SSO challenge rejected: no tenant subdomain supplied.");
            return Result<string>.Failure("tenant_required");
        }

        OpenIdConnectConfiguration config;
        try
        {
            config = await _oidcConfig.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSO challenge failed: could not load the Entra OIDC discovery document.");
            return Result<string>.Failure("sso_failed");
        }

        var (verifier, challenge) = CreatePkce();
        var nonce = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        var state = new SsoState(
            tenant,
            string.IsNullOrWhiteSpace(returnUrl) ? "/dashboard" : returnUrl!,
            string.IsNullOrWhiteSpace(returnOrigin) ? string.Empty : returnOrigin!,
            nonce,
            verifier);

        var protectedState = _stateProtector.Protect(
            JsonSerializer.Serialize(state), TimeSpan.FromMinutes(StateLifetimeMinutes));

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _options.RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = _options.Scopes,
            ["state"] = protectedState,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account",
        };

        var url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(config.AuthorizationEndpoint, query);
        return Result<string>.Success(url);
    }

    public async Task<Result<SsoSignInResult>> CompleteSignInAsync(
        string? code, string? state, string? error, string? errorDescription,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result<SsoSignInResult>.Failure("not_configured");
        }

        if (!string.IsNullOrEmpty(error))
        {
            // User cancelled consent, or Entra rejected the request (e.g. admin consent required).
            _logger.LogWarning("SSO callback returned error '{Error}': {Description}", error, errorDescription);
            // ISSUE-328: audit the IdP error. Entra echoes the signed state on error redirects — when it parses,
            // it is a trusted source of the HRM tenant; otherwise record a system-level failure (null tenant).
            var idpSubdomain = string.IsNullOrEmpty(state) ? null : TryUnprotectState(state)?.Subdomain;
            var idpReason = string.IsNullOrWhiteSpace(errorDescription) ? error! : $"{error}: {errorDescription}";
            await _authService.RecordSsoFailureAsync("sso_idp_error", idpSubdomain, idpReason, ipAddress, userAgent, cancellationToken);
            return Result<SsoSignInResult>.Failure("access_denied");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return Result<SsoSignInResult>.Failure("sso_failed");
        }

        SsoState? parsedState = TryUnprotectState(state);
        if (parsedState is null)
        {
            // ISSUE-328: the state is the ONLY trusted source of the HRM tenant; with none we cannot attribute this
            // to a tenant, so record a SYSTEM-LEVEL failure (null tenant) rather than trusting the request.
            await _authService.RecordSsoFailureAsync("sso_state_invalid", null, "state_could_not_be_validated", ipAddress, userAgent, cancellationToken);
            return Result<SsoSignInResult>.Failure("sso_failed");
        }

        OpenIdConnectConfiguration config;
        try
        {
            config = await _oidcConfig.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSO callback failed: could not load the Entra OIDC discovery document.");
            return Result<SsoSignInResult>.Failure("sso_failed");
        }

        // ── Exchange the authorization code for tokens (PKCE verifier from the signed state) ──
        var idToken = await ExchangeCodeAsync(config.TokenEndpoint, code, parsedState.CodeVerifier, cancellationToken);
        if (idToken is null)
        {
            // ISSUE-328: the code exchange failed — attribute to the tenant from the (validated) state.
            await _authService.RecordSsoFailureAsync("sso_token_validation_failed", parsedState.Subdomain, "code_exchange_failed", ipAddress, userAgent, cancellationToken);
            return Result<SsoSignInResult>.Failure("sso_failed");
        }

        // ── Validate the id_token: signature (JWKS), audience, issuer (custom per-tid), lifetime ──
        var validationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
            ValidateIssuer = true,
            IssuerValidator = ValidateMicrosoftIssuer,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
        };

        var handler = new JsonWebTokenHandler();
        var validation = await handler.ValidateTokenAsync(idToken, validationParameters);
        if (!validation.IsValid)
        {
            _logger.LogWarning(validation.Exception, "SSO callback rejected: id_token validation failed.");
            await _authService.RecordSsoFailureAsync("sso_token_validation_failed", parsedState.Subdomain, "id_token_validation_failed", ipAddress, userAgent, cancellationToken);
            return Result<SsoSignInResult>.Failure("sso_failed");
        }

        var jwt = (JsonWebToken)validation.SecurityToken;

        // Nonce binds the id_token to THIS browser session (replay protection).
        if (!jwt.TryGetPayloadValue<string>("nonce", out var tokenNonce) || tokenNonce != parsedState.Nonce)
        {
            _logger.LogWarning("SSO callback rejected: nonce mismatch.");
            await _authService.RecordSsoFailureAsync("sso_token_validation_failed", parsedState.Subdomain, "nonce_mismatch", ipAddress, userAgent, cancellationToken);
            return Result<SsoSignInResult>.Failure("sso_failed");
        }

        jwt.TryGetPayloadValue<string>("tid", out var tid);
        jwt.TryGetPayloadValue<string>("oid", out var oid);
        var email = GetEmail(jwt);
        jwt.TryGetPayloadValue<string>("name", out var displayName);

        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(oid) || string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("SSO callback rejected: id_token missing tid/oid/email claims.");
            await _authService.RecordSsoFailureAsync("sso_token_validation_failed", parsedState.Subdomain, "missing_required_claims", ipAddress, userAgent, cancellationToken);
            return Result<SsoSignInResult>.Failure("sso_failed");
        }

        // ── US-AUTH-013 tenant-isolation guard (FAIL-CLOSED) ──
        var guard = CheckIsolation(parsedState.Subdomain, tid!, email);
        if (!guard.Allowed)
        {
            return Result<SsoSignInResult>.Failure("access_denied");
        }

        var identity = new SsoIdentity
        {
            Subdomain = parsedState.Subdomain,
            TenantId = tid!,
            ObjectId = oid!,
            Email = email,
            DisplayName = displayName,
            JitAllowed = guard.JitAllowed,
            DefaultRole = guard.DefaultRole,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        };

        var signIn = await _authService.SsoSignInAsync(identity, cancellationToken);
        if (signIn.IsFailure)
        {
            // Reason is logged/audited inside the auth service; surface a generic fail-closed code.
            _logger.LogWarning("SSO sign-in denied for tenant '{Subdomain}': {Reason}", parsedState.Subdomain, signIn.Error);
            return Result<SsoSignInResult>.Failure("access_denied");
        }

        return Result<SsoSignInResult>.Success(new SsoSignInResult
        {
            Login = signIn.Value!,
            ReturnOrigin = parsedState.ReturnOrigin,
            ReturnUrl = parsedState.ReturnUrl,
        });
    }

    // ── Admin-consent onboarding (US-AUTH-016) ────────────────────────────────────────────────

    public Task<Result<string>> BuildAdminConsentUrlAsync(
        string? subdomain, string? returnOrigin, CancellationToken cancellationToken = default)
    {
        if (!IsAdminConsentConfigured)
        {
            return Task.FromResult(Result<string>.Failure("not_configured"));
        }

        var tenant = (subdomain ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(tenant))
        {
            _logger.LogInformation("Admin-consent rejected: no tenant subdomain supplied.");
            return Task.FromResult(Result<string>.Failure("tenant_required"));
        }

        var nonce = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var state = new AdminConsentState(
            tenant, string.IsNullOrWhiteSpace(returnOrigin) ? string.Empty : returnOrigin!, nonce);

        var protectedState = _adminConsentStateProtector.Protect(
            JsonSerializer.Serialize(state), TimeSpan.FromMinutes(StateLifetimeMinutes));

        // v2 admin-consent endpoint on the multi-tenant "organizations" authority. Consent grants the app's
        // configured delegated/app permissions tenant-wide; only client_id + redirect + state are required.
        var authority = _options.Authority.TrimEnd('/');
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.AdminConsentRedirectUri,
            ["scope"] = _options.Scopes,
            ["state"] = protectedState,
        };

        var url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString($"{authority}/adminconsent", query);
        return Task.FromResult(Result<string>.Success(url));
    }

    public async Task<Result<AdminConsentResult>> CompleteAdminConsentAsync(
        string? tenant, string? adminConsent, string? state, string? error, string? errorDescription,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        AdminConsentState? parsedState = TryUnprotectAdminConsentState(state);
        if (parsedState is null)
        {
            // The state is the only trustworthy source of the HRM tenant — with no valid state we cannot safely
            // attribute the outcome to any tenant. Fail closed (the caller redirects to a generic error page).
            // ISSUE-328: record a SYSTEM-LEVEL failure (null tenant) — we will not trust an unverified subdomain.
            await _authService.RecordSsoFailureAsync("sso_state_invalid", null, "admin_consent_state_could_not_be_validated", ipAddress, userAgent, cancellationToken);
            return Result<AdminConsentResult>.Failure("sso_failed");
        }

        var origin = parsedState.ReturnOrigin;

        // AC-6: a declined/failed consent, or a success flag that isn't affirmatively "True", or a missing
        // customer directory id → do NOT enable SSO; audit the failure and keep the prior mode.
        var granted = string.Equals(adminConsent, "True", StringComparison.OrdinalIgnoreCase)
                      && string.IsNullOrEmpty(error)
                      && !string.IsNullOrWhiteSpace(tenant);

        if (!granted)
        {
            var reason = !string.IsNullOrEmpty(error)
                ? $"{error}: {errorDescription}".Trim().TrimEnd(':').Trim()
                : "consent_declined_or_incomplete";
            _logger.LogWarning("Admin consent NOT granted for tenant '{Subdomain}': {Reason}", parsedState.Subdomain, reason);
            await _authService.RecordAdminConsentFailureAsync(parsedState.Subdomain, reason, ipAddress, userAgent, cancellationToken);
            return Result<AdminConsentResult>.Success(new AdminConsentResult { ReturnOrigin = origin, Succeeded = false });
        }

        // AC-5/FR-6: capture the customer's Entra Directory id into the resolved HRM tenant's allow-list. The HRM
        // tenant comes from the signed state's subdomain — NEVER from the consent 'tenant' (tid) itself (isolation).
        var capture = await _authService.CaptureAdminConsentAsync(parsedState.Subdomain, tenant!.Trim(), ipAddress, userAgent, cancellationToken);
        if (capture.IsFailure)
        {
            _logger.LogWarning("Admin consent capture failed for tenant '{Subdomain}': {Reason}", parsedState.Subdomain, capture.Error);
            return Result<AdminConsentResult>.Success(new AdminConsentResult { ReturnOrigin = origin, Succeeded = false });
        }

        return Result<AdminConsentResult>.Success(new AdminConsentResult { ReturnOrigin = origin, Succeeded = true });
    }

    private AdminConsentState? TryUnprotectAdminConsentState(string? state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AdminConsentState>(_adminConsentStateProtector.Unprotect(state));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin-consent callback rejected: state could not be validated.");
            return null;
        }
    }

    // ── Isolation guard ──────────────────────────────────────────────────────────────────────

    private (bool Allowed, bool JitAllowed, string? DefaultRole) CheckIsolation(string subdomain, string tid, string email)
    {
        if (!_options.TenantAllowList.TryGetValue(subdomain, out var allow) || allow is null || !allow.HasAnyAllowRule)
        {
            _logger.LogWarning(
                "SSO isolation REJECTED: HRM tenant '{Subdomain}' has no SSO allow-list configured (fail-closed). " +
                "Incoming Entra tid='{Tid}'. Add it to Authentication:Entra:TenantAllowList:{Subdomain}:AllowedTenantIds to permit.",
                subdomain, tid, subdomain);
            return (false, false, null);
        }

        var domain = EmailDomain(email);
        var tidAllowed = allow.AllowedTenantIds.Any(t => string.Equals(t, tid, StringComparison.OrdinalIgnoreCase));
        var domainAllowed = !string.IsNullOrEmpty(domain)
                            && allow.AllowedDomains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));

        if (!tidAllowed && !domainAllowed)
        {
            _logger.LogWarning(
                "SSO isolation REJECTED for HRM tenant '{Subdomain}': Entra tid='{Tid}' and email domain='{Domain}' " +
                "are not allow-listed. To permit this org, add tid '{Tid}' to " +
                "Authentication:Entra:TenantAllowList:{Subdomain}:AllowedTenantIds.",
                subdomain, tid, domain, tid, subdomain);
            return (false, false, null);
        }

        // JIT only when the email domain itself is allow-listed (a tid-only match won't auto-create accounts
        // for arbitrary domains in that directory).
        var jitAllowed = allow.JitProvisioning && domainAllowed;
        return (true, jitAllowed, allow.DefaultRole);
    }

    // ── Code exchange ────────────────────────────────────────────────────────────────────────

    private async Task<string?> ExchangeCodeAsync(string tokenEndpoint, string code, string codeVerifier, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = _options.Scopes,
        });

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(tokenEndpoint, content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSO code exchange failed: token endpoint request errored.");
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // Body may contain error/error_description but never log the full token response on success.
            _logger.LogWarning("SSO code exchange rejected by Entra ({Status}): {Body}", (int)response.StatusCode, body);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id_token", out var idTokenEl) ? idTokenEl.GetString() : null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "SSO code exchange failed: token response was not valid JSON.");
            return null;
        }
    }

    // ── id_token issuer validation (the /organizations authority issues per-tenant issuers) ──

    private static string ValidateMicrosoftIssuer(string issuer, SecurityToken securityToken, TokenValidationParameters parameters)
    {
        // For the multi-tenant "organizations" endpoint the issuer is templated per directory:
        // https://login.microsoftonline.com/{tid}/v2.0 — it MUST match the token's own tid claim.
        if (securityToken is JsonWebToken jwt
            && jwt.TryGetPayloadValue<string>("tid", out var tid)
            && !string.IsNullOrEmpty(tid))
        {
            var expected = $"https://login.microsoftonline.com/{tid}/v2.0";
            if (string.Equals(issuer, expected, StringComparison.OrdinalIgnoreCase))
            {
                return issuer;
            }
        }

        throw new SecurityTokenInvalidIssuerException($"Untrusted id_token issuer '{issuer}'.");
    }

    // ── State protection ─────────────────────────────────────────────────────────────────────

    private SsoState? TryUnprotectState(string state)
    {
        try
        {
            return JsonSerializer.Deserialize<SsoState>(_stateProtector.Unprotect(state));
        }
        catch (Exception ex)
        {
            // Tampered, expired, or forged state — fail closed.
            _logger.LogWarning(ex, "SSO callback rejected: state could not be validated.");
            return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static string GetEmail(JsonWebToken jwt)
    {
        if (jwt.TryGetPayloadValue<string>("email", out var email) && !string.IsNullOrWhiteSpace(email))
        {
            return email.Trim();
        }

        // Fall back to the UPN, which for work/school accounts is normally the email address.
        if (jwt.TryGetPayloadValue<string>("preferred_username", out var upn) && upn.Contains('@'))
        {
            return upn.Trim();
        }

        return string.Empty;
    }

    private static string EmailDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..].ToLowerInvariant() : string.Empty;
    }

    private static (string verifier, string challenge) CreatePkce()
    {
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record SsoState(
        string Subdomain, string ReturnUrl, string ReturnOrigin, string Nonce, string CodeVerifier);

    /// <summary>US-AUTH-016: the signed admin-consent state — carries the HRM tenant subdomain (the ONLY trusted
    /// source of the HRM tenant on the callback) + the SPA origin to return to + an anti-replay nonce.</summary>
    private sealed record AdminConsentState(string Subdomain, string ReturnOrigin, string Nonce);
}
