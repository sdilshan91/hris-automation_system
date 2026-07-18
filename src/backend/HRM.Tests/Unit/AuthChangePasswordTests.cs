// ============================================================================
// ISSUE-248 regression: authenticated self-service change-password. There was no
// logged-in change-password path (password history was enforced only on the token
// reset path). The new POST /auth/change-password verifies the CURRENT password and
// runs the new one through the SAME tenant password-policy + history rules as reset.
//
// Provider: EF InMemory through the real AuthService (the logic under test — current-
// password verification + history reuse — is provider-independent).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
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

public sealed class AuthChangePasswordTests
{
    private const string CurrentPassword = "CurrentPassw0rd!";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public AuthChangePasswordTests()
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
            }).Build();
        _jwtService = new JwtService(_configuration);
    }

    /// <summary>Seeds the tenant (with an optional password-history count) and the user.</summary>
    private async Task SeedAsync(int passwordHistoryCount = 0)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme Corp", Status = TenantStatus.Active,
            MinPasswordLength = 12, RequireUppercase = true, RequireLowercase = true,
            RequireDigit = true, RequireSpecialCharacter = true,
            PasswordHistoryCount = passwordHistoryCount,
        });
        db.Users.Add(new User
        {
            Id = _userId, Email = "user@acme.com", IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(CurrentPassword, workFactor: 12),
        });
        await db.SaveChangesAsync();
    }

    private async Task<User> ReloadUserAsync()
    {
        using var db = CreateDbContext();
        return await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_IsRejected_AndPasswordUnchanged()
    {
        await SeedAsync();

        var result = await CreateService().ChangePasswordAsync(
            _userId, "NotMyPassw0rd!", "BrandNewPassw0rd!", null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        var user = await ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash).Should().BeTrue("a wrong current password must not change the hash");
    }

    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_Succeeds_AndHashChanges()
    {
        await SeedAsync();

        var result = await CreateService().ChangePasswordAsync(
            _userId, CurrentPassword, "BrandNewPassw0rd!", "10.0.0.1", "Chrome");

        result.IsSuccess.Should().BeTrue();
        var user = await ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify("BrandNewPassw0rd!", user.PasswordHash).Should().BeTrue("the password must be updated");
        BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash).Should().BeFalse("the old password must no longer work");
    }

    [Fact]
    public async Task ChangePassword_ReusingCurrentPassword_IsRejected_ByHistory()
    {
        // History enabled: the CURRENT password is seeded into the comparison, so changing to it is blocked.
        await SeedAsync(passwordHistoryCount: 3);

        var result = await CreateService().ChangePasswordAsync(
            _userId, CurrentPassword, CurrentPassword, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("password_reused");
    }

    [Fact]
    public async Task ChangePassword_ReusingAPreviousPassword_IsRejected_ByHistory()
    {
        await SeedAsync(passwordHistoryCount: 3);

        // First change: Current -> P2 (records Current + P2 in history).
        (await CreateService().ChangePasswordAsync(_userId, CurrentPassword, "SecondPassw0rd!", null, null))
            .IsSuccess.Should().BeTrue();

        // Attempt to change P2 -> the original Current password: rejected by history (reuse of the last N).
        var reuse = await CreateService().ChangePasswordAsync(_userId, "SecondPassw0rd!", CurrentPassword, null, null);

        reuse.IsFailure.Should().BeTrue();
        reuse.StatusCode.Should().Be(400);
        reuse.ErrorCode.Should().Be("password_reused");
        var user = await ReloadUserAsync();
        BCrypt.Net.BCrypt.Verify("SecondPassw0rd!", user.PasswordHash).Should().BeTrue("the rejected reuse must not change the hash");
    }

    private AuthService CreateService() => new(
        CreateDbContext(), _jwtService, _tenantContext, Substitute.For<ITotpService>(), _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(), Substitute.For<Hangfire.IBackgroundJobClient>());

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);
}
