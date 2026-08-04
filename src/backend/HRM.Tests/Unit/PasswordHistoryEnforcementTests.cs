// ============================================================================
// IEEE-829 regression suite — password-history enforcement (US-AUTH-004 FR-5 / ISSUE-053).
//
// Landed change under test (branch fix/auth-hardening-b):
//   * PasswordHistoryValidator.IsReused(candidate, priorHashes, verify) — pure reuse check.
//   * PasswordHistory entity/DbSet/config (migration AddPasswordHistory).
//   * AuthService.ResetPasswordAsync: rejects "password_reused" (400) when the new password
//     matches any of the last Tenant.PasswordHistoryCount hashes (current password seeded
//     into the comparison); records the new hash and prunes beyond N; skipped when count <= 0.
//
// Pre-fix these fail because IsReused did not exist, there was no PasswordHistory table, and
// ResetPasswordAsync happily accepted a reused (even identical-to-current) password.
//
// Provider: real BCrypt.Verify delegate + EF InMemory through the real AuthService (the
// reuse/prune logic is provider-independent; it is the same pattern as AuthPasswordResetTests).
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class PasswordHistoryEnforcementTests
{
    private static readonly Func<string, string, bool> BcryptVerify =
        (pw, hash) => BCrypt.Net.BCrypt.Verify(pw, hash);

    private static string Bcrypt(string pw) => BCrypt.Net.BCrypt.HashPassword(pw, workFactor: 12);

    // ===================== PasswordHistoryValidator.IsReused (pure unit) =====================

    // -------- TC-AUTH-PWH-01: a candidate that BCrypt-matches a prior hash is detected --------
    [Fact]
    public void PwHistory_ReusedHash_Detected_ISSUE053()
    {
        const string candidate = "ReusedPassw0rd!";
        var priorHashes = new string?[] { Bcrypt("SomethingElse1!"), Bcrypt(candidate) };

        PasswordHistoryValidator.IsReused(candidate, priorHashes, BcryptVerify)
            .Should().BeTrue("the candidate matches one of the prior BCrypt hashes");
    }

    // -------- TC-AUTH-PWH-02: a fresh password matches nothing --------
    [Fact]
    public void PwHistory_FreshPassword_NotReused()
    {
        var priorHashes = new string?[] { Bcrypt("OldOne1234!"), Bcrypt("OldTwo1234!") };

        PasswordHistoryValidator.IsReused("CompletelyNew9!", priorHashes, BcryptVerify)
            .Should().BeFalse();
    }

    // -------- TC-AUTH-PWH-03: empty history is never a reuse --------
    [Fact]
    public void PwHistory_EmptyHistory_NotReused()
    {
        PasswordHistoryValidator.IsReused("Anything123!", Array.Empty<string?>(), BcryptVerify)
            .Should().BeFalse();
    }

    // -------- TC-AUTH-PWH-04: null/empty entries in the list are ignored, not passed to verify --------
    [Fact]
    public void PwHistory_NullAndEmptyHashes_Ignored()
    {
        const string candidate = "MatchMe12345!";
        // If null/empty were passed to BCrypt.Verify it would throw; they must be skipped.
        var withOnlyBlanks = new string?[] { null, string.Empty };
        PasswordHistoryValidator.IsReused(candidate, withOnlyBlanks, BcryptVerify)
            .Should().BeFalse("blank entries carry no password and must be skipped");

        var blanksThenMatch = new string?[] { null, string.Empty, Bcrypt(candidate) };
        PasswordHistoryValidator.IsReused(candidate, blanksThenMatch, BcryptVerify)
            .Should().BeTrue("a real match after blanks must still be found");
    }

    // ===================== ResetPasswordAsync integration =====================

    private const string P0 = "InitialPassw0rd!";
    private const string P1 = "FirstRotationP1!";
    private const string P2 = "SecondRotationP2!";

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // -------- TC-AUTH-PWH-05: resetting to the CURRENT password is rejected (400 password_reused) --------
    [Fact]
    public async Task Reset_ReuseCurrentPassword_Rejected_AUTH004()
    {
        var ctx = new ResetFixture(historyCount: 2);
        await ctx.SeedUserAsync(currentPassword: P0);
        await ctx.SetResetTokenAsync("tok-a");

        var result = await ctx.Service().ResetPasswordAsync("tok-a", P0);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("password_reused");

        var user = await ctx.ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify(P0, user.PasswordHash).Should().BeTrue("the password must not change on a rejected reset");
        user.PasswordResetTokenHash.Should().NotBeNull("a rejected reset must not consume the token");
    }

    // -------- TC-AUTH-PWH-06: a brand-new password succeeds and is recorded in history --------
    [Fact]
    public async Task Reset_NewPassword_RecordsHistory()
    {
        var ctx = new ResetFixture(historyCount: 2);
        await ctx.SeedUserAsync(currentPassword: P0);
        await ctx.SetResetTokenAsync("tok-b");

        var result = await ctx.Service().ResetPasswordAsync("tok-b", P1);

        result.IsSuccess.Should().BeTrue();

        var user = await ctx.ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify(P1, user.PasswordHash).Should().BeTrue();

        var history = await ctx.HistoryHashesAsync();
        history.Should().Contain(h => BCrypt.Net.BCrypt.Verify(P1, h),
            "the new password hash must be persisted to PasswordHistory");
    }

    // -------- TC-AUTH-PWH-07: reusing a recent (within last-N) password is rejected --------
    [Fact]
    public async Task Reset_ReuseRecentPassword_Rejected_AUTH004()
    {
        var ctx = new ResetFixture(historyCount: 2);
        await ctx.SeedUserAsync(currentPassword: P0);

        await ctx.RotateAsync("tok-1", P1); // P0 -> P1
        await ctx.RotateAsync("tok-2", P2); // P1 -> P2 ; history window now {P1, P2}

        await ctx.SetResetTokenAsync("tok-3");
        var result = await ctx.Service().ResetPasswordAsync("tok-3", P1);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("password_reused", "P1 is still inside the last-N window");
    }

    // -------- TC-AUTH-PWH-08: a password older than N (pruned out of the window) is allowed again --------
    [Fact]
    public async Task Reset_PasswordOlderThanN_Allowed()
    {
        var ctx = new ResetFixture(historyCount: 2);
        await ctx.SeedUserAsync(currentPassword: P0);

        await ctx.RotateAsync("tok-1", P1); // P0 -> P1
        await ctx.RotateAsync("tok-2", P2); // P1 -> P2 ; P0 is now pruned beyond N=2

        await ctx.SetResetTokenAsync("tok-3");
        var result = await ctx.Service().ResetPasswordAsync("tok-3", P0);

        result.IsSuccess.Should().BeTrue("P0 was pruned out of the last-N window and may be reused");
        var user = await ctx.ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify(P0, user.PasswordHash).Should().BeTrue();
    }

    // -------- TC-AUTH-PWH-09: history disabled (count=0) skips the check entirely --------
    [Fact]
    public async Task Reset_HistoryDisabled_AllowsReuse()
    {
        var ctx = new ResetFixture(historyCount: 0);
        await ctx.SeedUserAsync(currentPassword: P0);
        await ctx.SetResetTokenAsync("tok-z");

        // Reset straight back to the current password: allowed because the check is skipped.
        var result = await ctx.Service().ResetPasswordAsync("tok-z", P0);

        result.IsSuccess.Should().BeTrue();
        var history = await ctx.HistoryHashesAsync();
        history.Should().BeEmpty("with history disabled no PasswordHistory rows are recorded");
    }

    /// <summary>Per-test fixture: an in-memory tenant + user with a configurable PasswordHistoryCount.</summary>
    private sealed class ResetFixture
    {
        public string Email { get; } = "reset-user@acme.com";
        private readonly Guid _tenantId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly int _historyCount;
        private readonly ITenantContext _tenantContext;
        private readonly IConfiguration _configuration;

        public ResetFixture(int historyCount)
        {
            _historyCount = historyCount;
            _tenantContext = Substitute.For<ITenantContext>();
            _tenantContext.TenantId.Returns(_tenantId);
            _tenantContext.IsResolved.Returns(true);
            _tenantContext.IsSystemContext.Returns(false);

            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "hrm-api-test",
                    ["Jwt:Audience"] = "hrm-client-test",
                    ["Platform:BaseDomain"] = "yourhrm.test",
                })
                .Build();
        }

        private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

        public async Task SeedUserAsync(string currentPassword)
        {
            using var db = Db();
            db.Tenants.Add(new Tenant
            {
                Id = _tenantId,
                Subdomain = "acme",
                Name = "Acme Corp",
                PasswordHistoryCount = _historyCount,
            });
            db.Users.Add(new User
            {
                Id = _userId,
                Email = Email,
                PasswordHash = Bcrypt(currentPassword),
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        public async Task SetResetTokenAsync(string rawToken)
        {
            using var db = Db();
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.PasswordResetTokenHash = Sha256Hex(rawToken);
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }

        /// <summary>Sets a token then performs a reset that is expected to succeed.</summary>
        public async Task RotateAsync(string rawToken, string newPassword)
        {
            await SetResetTokenAsync(rawToken);
            var result = await Service().ResetPasswordAsync(rawToken, newPassword);
            result.IsSuccess.Should().BeTrue($"rotation to {newPassword} should succeed");
        }

        public async Task<User> ReloadUserAsync()
        {
            using var db = Db();
            return await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        }

        public async Task<List<string>> HistoryHashesAsync()
        {
            using var db = Db();
            return await db.PasswordHistories
                .IgnoreQueryFilters()
                .Where(ph => ph.UserId == _userId)
                .Select(ph => ph.PasswordHash)
                .ToListAsync();
        }

        public AuthService Service() => new(
            Db(),
            new JwtService(_configuration),
            _tenantContext,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>());
    }
}
