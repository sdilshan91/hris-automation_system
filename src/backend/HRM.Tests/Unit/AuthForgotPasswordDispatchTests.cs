// ============================================================================
// US-NTF-006 Phase 2b — self-service password-reset email dispatch.
// AuthService.ForgotPasswordAsync now dispatches a `password_reset` email via
// the (optional, nullable) INotificationDispatcher. These tests assert the
// dispatch contract WITHOUT weakening the existing token flow (unchanged):
//   • valid tenant member  → SendEmailAsync ONCE, EventKey=="password_reset",
//     RecipientUserId==user.Id, reset.url embeds the RAW token, still Success.
//   • unknown email         → NO dispatch (no-enumeration preserved), Success.
//   • dispatcher throws      → swallowed, request still Success.
//
// Provider: EF InMemory through the real AuthService (mirrors AuthPasswordResetTests).
// The INotificationDispatcher is substituted (NSubstitute) so we assert Received
// against the NotificationRequest rather than a delivery row.
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
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

public sealed class AuthForgotPasswordDispatchTests
{
    private const string Email = "member@acme.com";
    private const string Subdomain = "acme";
    private const string BaseDomain = "yourhrm.test";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    public AuthForgotPasswordDispatchTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
        _tenantContext.Subdomain.Returns(Subdomain);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "hrm-api-test",
                ["Jwt:Audience"] = "hrm-client-test",
                ["Platform:BaseDomain"] = BaseDomain,
            })
            .Build();

        _jwtService = new JwtService(_configuration);
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Seeds a user, tenant and active membership so ForgotPassword issues a token + dispatches.</summary>
    private async Task SeedTenantMemberAsync()
    {
        using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Id = _userId,
            Email = Email,
            DisplayName = "Member One",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassw0rd!", workFactor: 12),
            IsActive = true,
        });
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = Subdomain, Name = "Acme Corp" });
        db.UserTenants.Add(new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
        });
        await db.SaveChangesAsync();
    }

    private async Task<User> ReloadUserAsync()
    {
        using var db = CreateDbContext();
        return await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
    }

    // ── #1 valid tenant member → dispatch ONCE, password_reset, RecipientUserId==user.Id,
    //        reset.url embeds the RAW token, still Result.Success() ─────────────────────────────
    [Fact]
    public async Task ForgotPassword_ForTenantMember_DispatchesPasswordResetEmail_WithTokenInUrl()
    {
        await SeedTenantMemberAsync();
        NotificationRequest? captured = null;
        _dispatcher
            .SendEmailAsync(Arg.Do<NotificationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await CreateService().ForgotPasswordAsync(Email);

        // No token in the API response — reset stays an unconditional success (no-enumeration).
        result.IsSuccess.Should().BeTrue();

        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        captured.Should().NotBeNull();
        captured!.EventKey.Should().Be("password_reset");
        captured.RecipientUserId.Should().Be(_userId);
        captured.TenantId.Should().Be(_tenantId);

        // The reset link embeds the RAW one-time token; assert the token value is present in the URL and that it
        // is the *real* token (its SHA-256 hash matches what was persisted) — proving the URL carries the credential.
        using var doc = JsonDocument.Parse(captured.PayloadJson);
        var resetUrl = doc.RootElement.GetProperty("reset").GetProperty("url").GetString();
        resetUrl.Should().NotBeNullOrWhiteSpace();
        resetUrl!.Should().Contain("reset-password?token=");

        var rawTokenInUrl = resetUrl.Split("reset-password?token=", 2)[1];
        rawTokenInUrl.Should().NotBeNullOrWhiteSpace("the raw reset token must be present in the URL");

        var user = await ReloadUserAsync();
        user.PasswordResetTokenHash.Should().Be(Sha256Hex(rawTokenInUrl),
            "the token in the reset link must be the real single-use token (its hash is what's stored)");
    }

    // ── #2 unknown email → NO user → NO dispatch (no leak), still Success ────────────────────────
    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_ReturnsSuccess_AndNeverDispatches()
    {
        // No user seeded at all → the email maps to nobody.
        var result = await CreateService().ForgotPasswordAsync("nobody@nowhere.com");

        result.IsSuccess.Should().BeTrue("no-enumeration: the response is unconditionally success");
        await _dispatcher.DidNotReceive().SendEmailAsync(
            Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().SendInAppAsync(
            Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    // ── #3 dispatcher throws → swallowed, reset request still succeeds ───────────────────────────
    [Fact]
    public async Task ForgotPassword_WhenDispatcherThrows_StillReturnsSuccess()
    {
        await SeedTenantMemberAsync();
        _dispatcher
            .When(d => d.SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("smtp down"));

        var result = await CreateService().ForgotPasswordAsync(Email);

        result.IsSuccess.Should().BeTrue("a delivery failure must not break the reset request");
        // The token was still persisted despite the delivery failure (reset not broken).
        var user = await ReloadUserAsync();
        user.PasswordResetTokenHash.Should().NotBeNullOrWhiteSpace();
        user.PasswordResetTokenExpiresAt.Should().NotBeNull();
    }

    private AuthService CreateService() =>
        new(
            CreateDbContext(),
            _jwtService,
            _tenantContext,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>(),
            notificationDispatcher: _dispatcher);

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);
}
