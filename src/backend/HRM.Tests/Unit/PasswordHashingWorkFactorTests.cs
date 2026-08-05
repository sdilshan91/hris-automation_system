// ============================================================================
// ISSUE-203 — BCrypt cost factor: configurable, floored, and self-migrating.
//
// Login is CPU-bound on ONE BCrypt verify. Measured on 8 cores: ~607 ms at factor 12 (~13 logins/sec, p95
// ~3.9 s at 20 VU against an 800 ms SLA), ~370 ms at 11, ~149 ms at 10. The finding was not "too many hashes"
// — the two dummy verifies in AuthService sit on mutually exclusive early-return branches — it was that the
// single unavoidable hash was priced too high with no way to change it.
//
// These arms pin the three properties that make the setting safe to own: it is honoured, it cannot be set
// below the OWASP floor, and changing it actually migrates existing users (otherwise the SLA never moves and
// the setting looks broken).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Security;
using HRM.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace HRM.Tests.Unit;

public sealed class PasswordHashingWorkFactorTests
{
    private static IServiceCollection BuildWith(string? workFactor)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused",
            ["Encryption:ActiveKeyId"] = "k1",
            ["Encryption:Keys:k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };

        if (workFactor is not null)
            settings[$"{PasswordHashingOptions.SectionName}:WorkFactor"] = workFactor;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddInfrastructure(configuration);
        return services;
    }

    /// <summary>Reads the cost factor out of a bcrypt hash string (<c>$2a$11$…</c>).</summary>
    private static int WorkFactorOf(string hash) =>
        int.Parse(hash.Split('$', StringSplitOptions.RemoveEmptyEntries)[1]);

    // ── The floor ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("9")]
    [InlineData("4")]
    [InlineData("0")]
    public void A_work_factor_below_the_OWASP_floor_refuses_to_start_ISSUE203(string tooLow)
    {
        // Lowering the cost is a legitimate throughput decision; lowering it too far silently weakens every
        // stored password, and that only becomes visible at breach time.
        var act = () => BuildWith(tooLow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WorkFactor*", "the error must name the setting the operator has to change");
    }

    [Fact]
    public void The_configured_default_starts_and_is_at_least_the_floor_ISSUE203()
    {
        var act = () => BuildWith(null);

        act.Should().NotThrow();
        PasswordHashingOptions.DefaultWorkFactor.Should().BeGreaterThanOrEqualTo(
            PasswordHashingOptions.MinimumWorkFactor,
            "the shipped default must itself satisfy the guard, or the app cannot boot unconfigured");
    }

    [Fact]
    public void The_default_was_lowered_from_the_historical_12_ISSUE203()
    {
        // The whole point of the finding. If someone restores 12 as the default, login throughput silently
        // returns to ~13/sec on 8 cores and the p95 SLA breaks again — with no test failing anywhere else.
        PasswordHashingOptions.DefaultWorkFactor.Should().BeLessThan(12,
            "cost 12 measured ~607ms/verify, capping login at ~13/sec against an 800ms p95 SLA at 20 VU");
    }

    // ── The setting is actually honoured ────────────────────────────────

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void Hashing_honours_the_configured_factor_ISSUE203(int factor)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("CorrectHorseBattery!1", workFactor: factor);

        WorkFactorOf(hash).Should().Be(factor);
    }

    // ── Migration — without this the setting is cosmetic ────────────────

    [Fact]
    public void A_hash_stored_at_the_OLD_factor_is_detectably_different_ISSUE203()
    {
        // This is what the re-hash-on-login check keys off. If the factor could not be read back out of the
        // stored hash, existing users could never be migrated and changing the setting would only affect new
        // passwords — every current user would keep paying the old cost forever.
        var old = BCrypt.Net.BCrypt.HashPassword("CorrectHorseBattery!1", workFactor: 12);
        var current = BCrypt.Net.BCrypt.HashPassword("CorrectHorseBattery!1", workFactor: 11);

        WorkFactorOf(old).Should().Be(12);
        WorkFactorOf(current).Should().Be(11);
        WorkFactorOf(old).Should().NotBe(WorkFactorOf(current));
    }

    [Fact]
    public void A_rehashed_password_still_verifies_ISSUE203()
    {
        // Re-hashing happens on the login path with the plaintext in hand. If the new hash did not verify, the
        // user would be locked out of their own account on their next sign-in — a far worse outcome than a
        // slow login.
        const string password = "CorrectHorseBattery!1";
        var old = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        BCrypt.Net.BCrypt.Verify(password, old).Should().BeTrue();

        var rehashed = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

        BCrypt.Net.BCrypt.Verify(password, rehashed).Should().BeTrue("the migrated hash must still authenticate");
        BCrypt.Net.BCrypt.Verify("wrong-password", rehashed).Should().BeFalse();
    }

    // ── The regression I nearly shipped ─────────────────────────────────

    [Fact]
    public void Login_performs_exactly_ONE_bcrypt_verify_ISSUE203()
    {
        // Source-level guard, because the cost is invisible to any behavioural assertion. The first version of
        // the re-hash change called Verify a second time to decide whether to migrate — doubling the exact CPU
        // cost this finding is about. Nothing would have failed; login would just have gone back to ~13/sec.
        var source = File.ReadAllText(RepoPath(
            "src", "backend", "HRM.Infrastructure", "Services", "AuthService.cs"));

        var loginSection = source[source.IndexOf("var passwordMatches", StringComparison.Ordinal)..];
        var untilNextMethod = loginSection[..loginSection.IndexOf("private", StringComparison.Ordinal)];

        untilNextMethod.Split("BCrypt.Net.BCrypt.Verify").Length.Should().Be(2,
            "the password must be verified ONCE and the result reused; a second Verify on the login path "
            + "doubles the per-login CPU cost that ISSUE-203 exists to reduce");
    }

    private static string RepoPath(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the repository root (identified by CLAUDE.md) must be locatable");
        return Path.Combine(new[] { dir!.FullName }.Concat(segments).ToArray());
    }
}
