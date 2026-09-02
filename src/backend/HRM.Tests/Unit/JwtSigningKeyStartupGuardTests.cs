// ============================================================================
// Queue item G2 — a blank Jwt:PrivateKey silently yields an ephemeral per-process signing key.
//
// JwtService falls back to RSA.Create(2048) when Jwt:PrivateKey is blank. JwtSigningKeyStartupGuard
// is the startup fail-fast that stops the application booting in that state outside Development.
//
// Two things are under test here, and they are deliberately different in kind:
//
//   1. The GUARD's decision table — which (key, environment) pairs are permitted. These are pure
//      unit arms over the real guard with a real IConfiguration and a real (fake-valued)
//      IHostEnvironment. No mocks of the code under test.
//
//   2. The HAZARD the guard's message CLAIMS — that a blank key makes independent instances reject
//      each other's tokens. EphemeralKey_InstancesRejectEachOthersTokens proves that against real
//      RSA signing and real validation rather than taking the comment's word for it. Without that
//      arm this file would assert only that a throw happens, never that the throw is warranted.
//
// Gating is ALLOW-LIST (permit only Development), not deny-list (forbid only Production). The
// NotDevelopment_* arms are the point: an unset, empty, misspelled or Staging environment must all
// FAIL. A deny-list guard passes every one of them, which is how GAP-015's Smtp:Host equivalent
// ends up fail-open under exactly the config omission it exists to catch.
// ============================================================================

using System.Collections.Generic;
using System.Security.Cryptography;
using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HRM.Tests.Unit;

public sealed class JwtSigningKeyStartupGuardTests
{
    // ── 1. The permitted development case still works ─────────────────────────
    // Local dev, `dotnet run` via launchSettings, docker.env.example, scripts/gen-openapi.sh, both
    // WebApplicationFactory fixtures and CI (ci-gate.yml:338) all run with ASPNETCORE_ENVIRONMENT=Development
    // and a blank key. If this arm fails, the guard has broken local development and CI.
    [Fact]
    public void BlankKey_InDevelopment_IsPermitted()
    {
        var act = () => JwtSigningKeyStartupGuard.EnsureSigningKeyIsConfigured(
            Config(privateKeyPem: ""), Env("Development"));

        act.Should().NotThrow(
            "the generated dev key is the documented local-development convenience — CI and both test hosts "
            + "boot with a blank Jwt:PrivateKey under Development");
    }

    // ── 2. The blank key fails outside Development ────────────────────────────
    // The whole decision table in one place. "Production" is the obvious case; the other four are the
    // reason this guard is allow-list gated — a deny-list on == "Production" permits every one of them.
    [Theory]
    [InlineData("Production", "the outage case: a per-process key in production")]
    [InlineData("Staging", "staging is multi-instance too; a deny-list on Production silently permits it")]
    [InlineData("Prodution", "a misspelled environment name must not disable the guard")]
    [InlineData("", "an EMPTY ASPNETCORE_ENVIRONMENT must not disable the guard")]
    [InlineData("QA", "a bespoke environment name must not disable the guard")]
    public void BlankKey_OutsideDevelopment_FailsStartup(string environmentName, string why)
    {
        var act = () => JwtSigningKeyStartupGuard.EnsureSigningKeyIsConfigured(
            Config(privateKeyPem: ""), Env(environmentName));

        act.Should().Throw<InvalidOperationException>(why);
    }

    // ── 3. The failure message names the actual consequence ───────────────────
    // A startup exception that says "Jwt:PrivateKey is not configured" tells an operator nothing about why
    // they should care. The two consequences are non-obvious and are what make this a stop-the-deploy fault,
    // so they are pinned: tokens invalidated on restart, and instances rejecting each other.
    [Fact]
    public void FailureMessage_NamesRestartInvalidationAndCrossInstanceRejection()
    {
        var act = () => JwtSigningKeyStartupGuard.EnsureSigningKeyIsConfigured(
            Config(privateKeyPem: ""), Env("Production"));

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().ContainEquivalentOf("Jwt:PrivateKey",
            "the operator must be told which setting to configure");
        message.Should().ContainEquivalentOf("INVALIDATES EVERY TOKEN ALREADY ISSUED",
            "consequence 1: a restart logs every user out mid-session");
        message.Should().ContainEquivalentOf("INSTANCES REJECT EACH OTHER'S TOKENS",
            "consequence 2: multi-instance deployments 401 intermittently");
        message.Should().Contain("Production",
            "naming the RESOLVED environment is what tells an operator whether the environment or the key is "
            + "the thing they got wrong");
        message.Should().ContainEquivalentOf("ASPNETCORE_ENVIRONMENT=Development",
            "a guard that blocks a legitimate local run without naming the escape hatch is a support ticket");
    }

    // ── 4. Whitespace is not a key ────────────────────────────────────────────
    // " " would pass a naive IsNullOrEmpty check, then fail deep inside RSA.ImportFromPem with a crypto
    // error that names neither the setting nor the consequence.
    [Fact]
    public void WhitespaceOnlyKey_IsTreatedAsAbsent()
    {
        var act = () => JwtSigningKeyStartupGuard.EnsureSigningKeyIsConfigured(
            Config(privateKeyPem: "   "), Env("Production"));

        act.Should().Throw<InvalidOperationException>(
            "a whitespace-only PEM cannot sign anything, so it must be treated as absent rather than passed "
            + "through to RSA.ImportFromPem");
    }

    // ── 5. A missing Jwt section entirely ─────────────────────────────────────
    // GetSection("Jwt").Get<JwtKeyRingOptions>() returns null when nothing binds. The guard must fail, not
    // NullReferenceException.
    [Fact]
    public void MissingJwtSectionEntirely_FailsStartupCleanly()
    {
        var empty = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var act = () => JwtSigningKeyStartupGuard.EnsureSigningKeyIsConfigured(empty, Env("Production"));

        act.Should().Throw<InvalidOperationException>(
            "no Jwt section at all is the same hazard as a blank key, and must surface as the same actionable "
            + "message rather than a NullReferenceException");
    }

    // ── 6. A configured key never throws, in any environment ──────────────────
    // The guard must be invisible to every correctly-configured deployment. Development is included so a
    // developer who DOES configure a key is not treated differently.
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("")]
    public void ConfiguredKey_IsPermittedInEveryEnvironment(string environmentName)
    {
        var pem = RSA.Create(2048).ExportRSAPrivateKeyPem();

        var act = () => JwtSigningKeyStartupGuard.EnsureSigningKeyIsConfigured(
            Config(privateKeyPem: pem), Env(environmentName));

        act.Should().NotThrow("a correctly configured signing key is the normal case and needs no environment "
                              + "reasoning at all");
    }

    // ── 7. The guard binds through the SAME path JwtService does ──────────────
    // JwtService reads GetSection("Jwt").Get<JwtKeyRingOptions>(). If the guard read the key some other way
    // (say configuration["Jwt:PrivateKey"] against a differently-shaped source) the two could disagree and the
    // guard would be checking something the service does not use. Prove agreement on the case that matters:
    // the guard passes exactly when JwtService gets a real key rather than a generated one.
    [Fact]
    public void GuardAgreesWithJwtService_AboutWhetherAKeyIsConfigured()
    {
        var pem = RSA.Create(2048).ExportRSAPrivateKeyPem();
        var configured = Config(privateKeyPem: pem);

        JwtSigningKeyStartupGuard.EnsureSigningKeyIsConfigured(configured, Env("Production"));

        // The service built from the very same configuration signs with the CONFIGURED key, not a generated
        // one — so a second, independently constructed service validates its token.
        var first = new JwtService(configured);
        var second = new JwtService(configured);

        second.ValidateAccessToken(IssueToken(first))
            .Should().NotBeNull(
                "the guard let this configuration through on the premise that JwtService uses the configured "
                + "key; if it used a generated one instead, the guard is checking a setting the service "
                + "ignores");
    }

    // ── 8. The hazard itself, proven rather than asserted ─────────────────────
    // Everything above tests the guard's DECISION. This tests the guard's PREMISE. Two JwtService instances
    // built from identical blank-key configuration model two instances of the app behind a load balancer (and
    // equally, the same instance before and after a restart). If they could validate each other's tokens
    // there would be no bug here and the guard would be pointless ceremony.
    [Fact]
    public void EphemeralKey_InstancesRejectEachOthersTokens()
    {
        var blank = Config(privateKeyPem: "");

        var instanceA = new JwtService(blank);
        var instanceB = new JwtService(blank);

        var tokenFromA = IssueToken(instanceA);

        instanceA.ValidateAccessToken(tokenFromA)
            .Should().NotBeNull("the issuing instance can of course validate its own token — which is exactly "
                                + "why this fault is invisible on a single-instance dev box");

        instanceB.ValidateAccessToken(tokenFromA)
            .Should().BeNull(
                "this IS the bug G2 exists for: identical configuration, yet the second instance generated a "
                + "different in-memory RSA key and rejects the first's token. Behind a load balancer that is "
                + "intermittent 401s; across a restart it is every user logged out");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly User TestUser = new()
    {
        Id = Guid.NewGuid(),
        Email = "guard.user@acme.test",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    private static string IssueToken(JwtService svc) => svc.GenerateAccessToken(
        TestUser, Guid.NewGuid(), Guid.NewGuid(), new[] { "HR Manager" }, new[] { "Employees.View" });

    private static IConfiguration Config(string privateKeyPem) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "hrm-api-guard-test",
                ["Jwt:Audience"] = "hrm-client-guard-test",
                ["Jwt:PrivateKey"] = privateKeyPem,
                ["Jwt:SigningKeyId"] = "hrm-guard-test-key",
            })
            .Build();

    private static IHostEnvironment Env(string environmentName) =>
        new FakeHostEnvironment { EnvironmentName = environmentName };

    /// <summary>
    /// Stands in for the RESOLVED host environment. The guard takes IHostEnvironment rather than the raw
    /// ASPNETCORE_ENVIRONMENT string precisely so it sees what the host resolved — including the
    /// UseEnvironment() call both WebApplicationFactory fixtures make, which never sets that variable.
    /// Only EnvironmentName is read; the rest satisfies the interface.
    /// </summary>
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "HRM.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
