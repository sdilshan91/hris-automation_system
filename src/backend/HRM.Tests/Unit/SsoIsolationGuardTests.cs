// ============================================================================
// GAP-002 / GAP-017 — direct tests for the US-AUTH-013 tenant-isolation decision.
//
// Why this file did not exist before: the decision lived inside EntraSsoService, reachable only through a
// full OIDC callback (signed state → code exchange → JWKS validation), so none of the 80 passing SSO tests
// touched a single one of its branches. That is how it went unnoticed that the guard read an appsettings
// dictionary while the per-tenant DB columns a tenant admin edits had ZERO read sites on any login path —
// which meant editing the allow-list in the UI changed nothing, admin-consent onboarding could not enable
// anyone, and SsoEnabled = false did not block SSO.
//
// The arms below are the negative cases specifically: this control's job is to REFUSE, and a control whose
// refusals are untested is a control on paper.
// ============================================================================

using FluentAssertions;
using HRM.Application.Features.Auth;
using HRM.Application.Features.Auth.DTOs;

namespace HRM.Tests.Unit;

public sealed class SsoIsolationGuardTests
{
    private const string CustomerTid = "11111111-1111-1111-1111-111111111111";
    private const string OtherTid = "22222222-2222-2222-2222-222222222222";

    private static SsoSettingsSnapshot Settings(
        bool enabled = true,
        string[]? tids = null,
        string[]? domains = null,
        bool jit = false,
        string? jitRole = null) => new()
        {
            SsoEnabled = enabled,
            AllowedEntraTenantIds = [.. tids ?? [CustomerTid]],
            AllowedEmailDomains = [.. domains ?? []],
            JitEnabled = jit,
            JitDefaultRole = jitRole,
        };

    // ── The switch the login path never read ────────────────────────────────

    [Fact]
    public void SsoDisabledForTenant_IsRefused_EvenWhenTheDirectoryIsAllowListed()
    {
        var decision = SsoIsolationGuard.Evaluate(
            Settings(enabled: false), CustomerTid, "user@customer.com", emailVerified: true);

        decision.Allowed.Should().BeFalse(
            "a tenant that has switched SSO off must not be signed into -- before GAP-002 no login path read "
            + "SsoEnabled at all, so turning SSO off in the UI did nothing");
        decision.Reason.Should().Be("sso_disabled_for_tenant");
    }

    [Fact]
    public void EnabledButUnconfigured_FailsClosed_AndIsReportedAsMisconfigurationNotAttack()
    {
        var decision = SsoIsolationGuard.Evaluate(
            Settings(tids: [], domains: []), CustomerTid, "user@customer.com", emailVerified: true);

        decision.Allowed.Should().BeFalse("an empty allow-list permits nobody -- fail closed");
        decision.Reason.Should().Be("sso_misconfigured",
            "US-AUTH-013 AC-5 distinguishes 'nobody set this up' from 'someone tried to get in'; an operator "
            + "needs to tell those apart");
    }

    // ── Cross-tenant refusal: the whole point of the control ────────────────

    [Fact]
    public void ADifferentDirectory_IsRefused()
    {
        var decision = SsoIsolationGuard.Evaluate(
            Settings(tids: [CustomerTid]), OtherTid, "attacker@elsewhere.com", emailVerified: true);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("sso_isolation_rejected");
    }

    [Fact]
    public void TheAllowListedDirectory_IsAdmitted()
    {
        SsoIsolationGuard.Evaluate(Settings(), CustomerTid, "user@customer.com", emailVerified: true)
            .Allowed.Should().BeTrue();
    }

    // ── GAP-017 / AC-7: the verified-email requirement ──────────────────────

    [Fact]
    public void UnverifiedEmail_CannotSatisfyTheDomainRule_GAP017()
    {
        // The ONLY allow rule is the domain, and the token asserts no verified email.
        var decision = SsoIsolationGuard.Evaluate(
            Settings(tids: [], domains: ["customer.com"]),
            OtherTid, "impostor@customer.com", emailVerified: false);

        decision.Allowed.Should().BeFalse(
            "Entra does not guarantee the email claim is verified; in a directory that permits unverified "
            + "addresses, a user could set an address at an allow-listed domain and cross into another "
            + "customer's tenant on the domain rule alone (US-AUTH-013 AC-7, FR-5)");
        decision.Reason.Should().Be("sso_isolation_rejected");
        decision.DomainMatchedButUnverified.Should().BeTrue(
            "the near-miss must be surfaced -- it is what an operator needs when a user who 'should' be "
            + "allowed is not");
    }

    [Fact]
    public void VerifiedEmail_SatisfiesTheDomainRule_GAP017()
    {
        SsoIsolationGuard.Evaluate(
            Settings(tids: [], domains: ["customer.com"]),
            OtherTid, "user@customer.com", emailVerified: true)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void UnverifiedEmail_DoesNotBlockAnOtherwiseValidDirectoryMatch()
    {
        // The tid rule is cryptographically bound to the issuing directory and cannot be self-asserted, so an
        // unverified email must not turn a legitimate tid match into a refusal.
        var decision = SsoIsolationGuard.Evaluate(
            Settings(tids: [CustomerTid], domains: ["customer.com"]),
            CustomerTid, "user@customer.com", emailVerified: false);

        decision.Allowed.Should().BeTrue();
        decision.DomainMatchedButUnverified.Should().BeTrue("still worth logging");
    }

    // ── JIT gating ──────────────────────────────────────────────────────────

    [Fact]
    public void Jit_RequiresTheVerifiedDomainRule_NotMerelyADirectoryMatch()
    {
        var decision = SsoIsolationGuard.Evaluate(
            Settings(tids: [CustomerTid], domains: [], jit: true, jitRole: "Employee"),
            CustomerTid, "anyone@some-other-domain.com", emailVerified: true);

        decision.Allowed.Should().BeTrue();
        decision.JitAllowed.Should().BeFalse(
            "a tid-only match must not auto-create accounts for arbitrary domains inside that directory");
        decision.DefaultRole.Should().BeNull();
    }

    [Fact]
    public void Jit_IsRefusedForAnUnverifiedEmail_EvenOnAnAllowListedDomain()
    {
        SsoIsolationGuard.Evaluate(
            Settings(tids: [CustomerTid], domains: ["customer.com"], jit: true, jitRole: "Employee"),
            CustomerTid, "impostor@customer.com", emailVerified: false)
            .JitAllowed.Should().BeFalse("an unverified address must never be what provisions an account");
    }

    [Fact]
    public void Jit_IsAllowedOnAVerifiedAllowListedDomain_AndCarriesTheTenantsDefaultRole()
    {
        var decision = SsoIsolationGuard.Evaluate(
            Settings(tids: [], domains: ["customer.com"], jit: true, jitRole: "Employee"),
            OtherTid, "newbie@customer.com", emailVerified: true);

        decision.JitAllowed.Should().BeTrue();
        decision.DefaultRole.Should().Be("Employee",
            "the role must come from the TENANT's JitDefaultRole -- the appsettings DefaultRole it used to "
            + "read was invisible to the admin who configured it");
    }

    [Fact]
    public void JitDisabledOnTheTenant_BlocksProvisioning_ForAnOtherwiseValidDomainMatch()
    {
        SsoIsolationGuard.Evaluate(
            Settings(tids: [], domains: ["customer.com"], jit: false),
            OtherTid, "newbie@customer.com", emailVerified: true)
            .JitAllowed.Should().BeFalse();
    }

    // ── Matching details ────────────────────────────────────────────────────

    [Theory]
    [InlineData("USER@CUSTOMER.COM")]
    [InlineData("user@Customer.Com")]
    public void DomainMatchingIsCaseInsensitive(string email)
    {
        SsoIsolationGuard.Evaluate(
            Settings(tids: [], domains: ["customer.com"]), OtherTid, email, emailVerified: true)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void DirectoryIdMatchingIsCaseInsensitive()
    {
        SsoIsolationGuard.Evaluate(
            Settings(tids: [CustomerTid.ToUpperInvariant()]), CustomerTid, "user@customer.com", true)
            .Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void AnEmailWithNoDomain_CannotMatchTheDomainRule(string email)
    {
        SsoIsolationGuard.Evaluate(
            Settings(tids: [], domains: ["customer.com"]), OtherTid, email, emailVerified: true)
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void ASubdomainOfAnAllowListedDomain_DoesNotMatch()
    {
        // evil.customer.com is a different host; matching it would let anyone who controls a subdomain of a
        // customer's domain in. Exact match only.
        SsoIsolationGuard.Evaluate(
            Settings(tids: [], domains: ["customer.com"]),
            OtherTid, "attacker@evil.customer.com", emailVerified: true)
            .Allowed.Should().BeFalse();
    }
}
