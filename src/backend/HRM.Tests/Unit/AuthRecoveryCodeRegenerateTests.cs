// ============================================================================
// ISSUE-241 regression (US-AUTH-005 AC-7, TC-AUTH-030 step 8): a recovery-code login
// must tell the user how many recovery codes remain and prompt them to regenerate once
// the codes run low. Pre-fix the LoginResponse carried no such signal.
//
// Provider: EF InMemory through the real AuthService + real TotpService (recovery-code
// hashing/verification is provider-independent).
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

public sealed class AuthRecoveryCodeRegenerateTests
{
    private const string Email = "mfa-user@acme.com";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;
    private readonly TotpService _totpService = new();

    public AuthRecoveryCodeRegenerateTests()
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

    /// <summary>Seeds an MFA-enabled user + tenant/membership, plus <paramref name="rawCodes"/> unused recovery codes.</summary>
    private async Task SeedAsync(IReadOnlyList<string> rawCodes)
    {
        using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme Corp", Status = TenantStatus.Active,
        });
        db.Users.Add(new User
        {
            Id = _userId, Email = Email, DisplayName = "MFA User", IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4),
            MfaEnabled = true,
            MfaSecret = _totpService.GenerateSecret(),
        });
        var role = new Role { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Employee" };
        db.Roles.Add(role);
        var membership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(), UserId = _userId, TenantId = _tenantId, Status = UserTenantStatus.Active,
        };
        db.UserTenants.Add(membership);
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id, RoleId = role.Id, AssignedAt = DateTime.UtcNow, AssignedBy = "test",
        });

        foreach (var raw in rawCodes)
        {
            db.MfaRecoveryCodes.Add(new MfaRecoveryCode
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                CodeHash = _totpService.HashRecoveryCode(raw),
                UsedAt = null,
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RecoveryCodeLogin_WhenFewRemain_ReturnsRemainingCount_AndRegenerateFlag()
    {
        // 3 codes total; burning one leaves 2 (<= the low-water mark of 3) -> should prompt regenerate.
        var codes = new[] { "AAAAA-BBBBB", "CCCCC-DDDDD", "EEEEE-FFFFF" };
        await SeedAsync(codes);

        var result = await CreateService().VerifyMfaLoginAsync(Email, codes[0], "127.0.0.1", "Chrome");

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.RecoveryCodesRemaining.Should().Be(2, "one of three codes was consumed");
        result.Value.ShouldRegenerateRecoveryCodes.Should().BeTrue("only two codes remain — below the low-water mark");
    }

    [Fact]
    public async Task RecoveryCodeLogin_WhenPlentyRemain_ReturnsCount_ButNoRegeneratePrompt()
    {
        // 10 codes total; burning one leaves 9 (> 3) -> count surfaced, but no regenerate nudge yet.
        var codes = Enumerable.Range(0, 10).Select(i => $"CODE{i:D1}-XXXXX").ToArray();
        await SeedAsync(codes);

        var result = await CreateService().VerifyMfaLoginAsync(Email, codes[0], "127.0.0.1", "Chrome");

        result.IsSuccess.Should().BeTrue();
        result.Value!.RecoveryCodesRemaining.Should().Be(9);
        result.Value.ShouldRegenerateRecoveryCodes.Should().BeFalse("nine codes remain — well above the low-water mark");
    }

    private AuthService CreateService() => new(
        CreateDbContext(), _jwtService, _tenantContext, _totpService, _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(), Substitute.For<Hangfire.IBackgroundJobClient>());

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);
}
