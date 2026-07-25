// ============================================================================
// ISSUE-328 (US-AUTH-011 FR-8) — the SSO callback FAILURE paths must emit a
// STRUCTURED audit record (via IAuthService.RecordSsoFailureAsync) with the
// AC-named event, not only an ILogger line. This suite pins the audit *emission*
// on the failure branches of EntraSsoService that are reachable WITHOUT a live
// Entra token endpoint / signed state (the token-validation branches — which run
// AFTER a real code exchange + JWKS validation — are exercised at the seam level
// in SsoFailureAuditWriteTests, where the resolved-tenant attribution actually
// lives).
//
//   sso_idp_error     — Entra returned an error/deny on the callback (TC-AUTH-149)
//   sso_state_invalid — the signed single-use `state` could not be validated,
//                       on BOTH the login and admin-consent callbacks (TC-AUTH-144)
//
// Tenant attribution: with an untrusted/unparseable state there is no trusted HRM
// tenant, so a SYSTEM-LEVEL (null subdomain) audit is emitted — never an
// unverified tenant. Removing the audit call on any branch fails the arm
// (mutation-meaningful). Provider: the REAL EntraSsoService over a substituted
// IAuthService; all branches under test return BEFORE any OIDC network call.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-AUTH-149")]
public sealed class EntraSsoFailureAuditTests
{
    private readonly IAuthService _authService = Substitute.For<IAuthService>();

    private EntraSsoService CreateService()
    {
        var options = Options.Create(new EntraSsoOptions
        {
            Enabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://app.test/api/v1/auth/sso/callback",
            AdminConsentRedirectUri = "https://app.test/api/v1/auth/sso/admin-consent/callback",
        });

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://login.microsoftonline.com/organizations/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        return new EntraSsoService(
            options,
            DataProtectionProvider.Create("tests"),
            Substitute.For<IHttpClientFactory>(),
            configManager,
            _authService,
            Substitute.For<ILogger<EntraSsoService>>());
    }

    // ── sso_idp_error: Entra returned an error/deny on the callback ────────────

    [Fact]
    public async Task LoginCallback_WhenEntraReturnsError_Audits_SsoIdpError_SystemLevel()
    {
        // Entra echoes `state` on error redirects, but here the state is unparseable, so there is no trusted
        // HRM tenant → a SYSTEM-LEVEL (null subdomain) sso_idp_error audit must be emitted.
        var result = await CreateService().CompleteSignInAsync(
            code: null, state: "not-a-valid-state", error: "access_denied",
            errorDescription: "AADSTS65004: user declined consent",
            ipAddress: "203.0.113.9", userAgent: "xUnit", CancellationToken.None);

        result.IsFailure.Should().BeTrue();

        await _authService.Received(1).RecordSsoFailureAsync(
            Arg.Is("sso_idp_error"), Arg.Is<string?>(s => s == null), Arg.Any<string>(),
            Arg.Is("203.0.113.9"), Arg.Is("xUnit"), Arg.Any<CancellationToken>());
    }

    // ── sso_state_invalid: the signed `state` could not be validated (login) ───

    [Fact]
    [Trait("TC", "TC-AUTH-144")]
    public async Task LoginCallback_WhenStateInvalid_Audits_SsoStateInvalid_SystemLevel()
    {
        // A well-formed request (code + state present, no IdP error) but a tampered/forged state → the parse
        // fails BEFORE any token exchange → a SYSTEM-LEVEL sso_state_invalid audit (we cannot trust the state's
        // subdomain, so tenantId must be null).
        var result = await CreateService().CompleteSignInAsync(
            code: "auth-code", state: "tampered-state", error: null, errorDescription: null,
            ipAddress: "203.0.113.10", userAgent: "xUnit", CancellationToken.None);

        result.IsFailure.Should().BeTrue();

        await _authService.Received(1).RecordSsoFailureAsync(
            Arg.Is("sso_state_invalid"), Arg.Is<string?>(s => s == null), Arg.Any<string>(),
            Arg.Is("203.0.113.10"), Arg.Is("xUnit"), Arg.Any<CancellationToken>());
        // Guard: a state-invalid failure is NOT a token-validation failure.
        await _authService.DidNotReceive().RecordSsoFailureAsync(
            Arg.Is("sso_token_validation_failed"), Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ── sso_state_invalid: admin-consent callback ─────────────────────────────

    [Fact]
    [Trait("TC", "TC-AUTH-144")]
    public async Task AdminConsentCallback_WhenStateInvalid_Audits_SsoStateInvalid_SystemLevel()
    {
        var result = await CreateService().CompleteAdminConsentAsync(
            tenant: Guid.NewGuid().ToString(), adminConsent: "True", state: "tampered-state",
            error: null, errorDescription: null,
            ipAddress: "203.0.113.11", userAgent: "xUnit", CancellationToken.None);

        result.IsFailure.Should().BeTrue();

        await _authService.Received(1).RecordSsoFailureAsync(
            Arg.Is("sso_state_invalid"), Arg.Is<string?>(s => s == null), Arg.Any<string>(),
            Arg.Is("203.0.113.11"), Arg.Is("xUnit"), Arg.Any<CancellationToken>());
    }
}
