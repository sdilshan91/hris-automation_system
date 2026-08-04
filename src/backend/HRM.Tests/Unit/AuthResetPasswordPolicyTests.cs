// ============================================================================
// BUG-004 regression: password reset must enforce the TENANT's configured
// password policy (e.g. a stricter min length), not just the hardcoded validator
// defaults. A password that meets the defaults but violates the tenant policy is
// rejected (and the reset token is NOT consumed).
// ============================================================================

using System.Security.Cryptography;
using System.Text;
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

public sealed class AuthResetPasswordPolicyTests
{
    private const string Email = "user@acme.com";
    private const string RawToken = "the-real-token";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public AuthResetPasswordPolicyTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "hrm-api-test",
                ["Jwt:Audience"] = "hrm-client-test",
                ["Platform:BaseDomain"] = "yourhrm.test",
            }).Build();
        _jwtService = new JwtService(_configuration);
    }

    private static string Sha256Hex(string v) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v))).ToLowerInvariant();

    private async Task SeedAsync(int minPasswordLength)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme Corp", Status = TenantStatus.Active,
            MinPasswordLength = minPasswordLength,
            RequireUppercase = true, RequireLowercase = true, RequireDigit = true, RequireSpecialCharacter = true,
        });
        db.Users.Add(new User
        {
            Id = _userId, Email = Email, IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassw0rd!", workFactor: 12),
            PasswordResetTokenHash = Sha256Hex(RawToken),
            PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reset_PasswordShorterThanTenantPolicy_IsRejected_AndTokenNotConsumed()
    {
        await SeedAsync(minPasswordLength: 16);

        // 12 chars, full complexity — meets the hardcoded defaults but violates the tenant's 16-char policy.
        var result = await CreateService().ResetPasswordAsync(RawToken, "Abcdefgh123!");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);

        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.PasswordResetTokenHash.Should().NotBeNull("a policy failure must not consume the token");
        BCrypt.Net.BCrypt.Verify("OldPassw0rd!", user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task Reset_PasswordMeetingTenantPolicy_Succeeds()
    {
        await SeedAsync(minPasswordLength: 16);

        var result = await CreateService().ResetPasswordAsync(RawToken, "Abcdefghijk1234!"); // 16 chars

        result.IsSuccess.Should().BeTrue();
        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        BCrypt.Net.BCrypt.Verify("Abcdefghijk1234!", user.PasswordHash).Should().BeTrue();
    }

    private AuthService CreateService() => new(
        CreateDbContext(), _jwtService, _tenantContext, Substitute.For<ITotpService>(), _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(), Substitute.For<Hangfire.IBackgroundJobClient>());

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);
}
