// ============================================================================
// US-AUTH / BUG-040 regression: password reset must require a valid, unexpired,
// single-use token. Pre-fix, ResetPasswordAsync accepted ANY non-empty token —
// anyone who knew a victim's email could reset their password (account takeover).
//
// Provider: EF InMemory through the real AuthService (the defect is token-
// validation logic, provider-independent).
// ============================================================================

using System.Security.Cryptography;
using System.Text;
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

public sealed class AuthPasswordResetTests
{
    private const string Email = "victim@acme.com";
    private const string OldPassword = "OldPassw0rd!";
    private const string NewPassword = "BrandNewPassw0rd!";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public AuthPasswordResetTests()
    {
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

        _jwtService = new JwtService(_configuration);
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Seeds the victim with an optional pending reset token (hash + expiry).</summary>
    private async Task SeedUserAsync(string? rawToken, DateTime? expiresAt)
    {
        using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Id = _userId,
            Email = Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(OldPassword, workFactor: 12),
            IsActive = true,
            PasswordResetTokenHash = rawToken is null ? null : Sha256Hex(rawToken),
            PasswordResetTokenExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
    }

    private async Task<User> ReloadUserAsync()
    {
        using var db = CreateDbContext();
        return await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
    }

    [Fact]
    public async Task ResetPassword_WithNoPendingToken_IsRejected_AndPasswordUnchanged()
    {
        // The core account-takeover case: no reset was requested, yet a non-empty token is supplied.
        await SeedUserAsync(rawToken: null, expiresAt: null);
        var service = CreateService();

        var result = await service.ResetPasswordAsync(Email, "any-non-empty-token", NewPassword);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        var user = await ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify(OldPassword, user.PasswordHash).Should().BeTrue("the password must not change");
    }

    [Fact]
    public async Task ResetPassword_WithWrongToken_IsRejected_AndPasswordUnchanged()
    {
        await SeedUserAsync(rawToken: "the-real-token", expiresAt: DateTime.UtcNow.AddHours(1));
        var service = CreateService();

        var result = await service.ResetPasswordAsync(Email, "a-different-token", NewPassword);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        var user = await ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify(OldPassword, user.PasswordHash).Should().BeTrue();
        user.PasswordResetTokenHash.Should().NotBeNull("a wrong attempt must not consume the token");
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_IsRejected()
    {
        await SeedUserAsync(rawToken: "the-real-token", expiresAt: DateTime.UtcNow.AddMinutes(-1));
        var service = CreateService();

        var result = await service.ResetPasswordAsync(Email, "the-real-token", NewPassword);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        var user = await ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify(OldPassword, user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_Succeeds_AndConsumesToken()
    {
        await SeedUserAsync(rawToken: "the-real-token", expiresAt: DateTime.UtcNow.AddHours(1));
        var service = CreateService();

        var result = await service.ResetPasswordAsync(Email, "the-real-token", NewPassword);

        result.IsSuccess.Should().BeTrue();
        var user = await ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify(NewPassword, user.PasswordHash).Should().BeTrue("the password must be updated");
        user.PasswordResetTokenHash.Should().BeNull("the token must be single-use");
        user.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task ResetPassword_ReusingConsumedToken_IsRejected()
    {
        await SeedUserAsync(rawToken: "the-real-token", expiresAt: DateTime.UtcNow.AddHours(1));

        (await CreateService().ResetPasswordAsync(Email, "the-real-token", NewPassword)).IsSuccess.Should().BeTrue();

        var second = await CreateService().ResetPasswordAsync(Email, "the-real-token", "AnotherPassw0rd!");

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ForgotPassword_ForTenantMember_IssuesHashedExpiringToken()
    {
        await SeedUserAsync(rawToken: null, expiresAt: null);
        using (var db = CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp" });
            db.UserTenants.Add(new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _tenantId,
                Status = UserTenantStatus.Active,
            });
            await db.SaveChangesAsync();
        }
        var service = CreateService();

        var result = await service.ForgotPasswordAsync(Email);

        result.IsSuccess.Should().BeTrue();
        var user = await ReloadUserAsync();
        user.PasswordResetTokenHash.Should().NotBeNullOrWhiteSpace();
        user.PasswordResetTokenExpiresAt.Should().NotBeNull().And.Subject.Should().BeAfter(DateTime.UtcNow);
    }

    private AuthService CreateService()
    {
        return new AuthService(
            CreateDbContext(),
            _jwtService,
            _tenantContext,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>());
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);
}
