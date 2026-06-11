using FluentAssertions;
using Hangfire;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Infrastructure.Identity;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit;

/// <summary>
/// Unit tests for US-AUTH-010 Account Lockout After Failed Attempts.
/// Covers AC-1 through AC-6, progressive lockout, MFA-counts-toward-lockout,
/// admin unlock (including BR-3 cross-tenant block), and password-reset-clears-lockout.
/// </summary>
public sealed class AccountLockoutTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public AccountLockoutTests()
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
        _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    }

    #region AC-1: Failed Login Below Threshold

    [Fact]
    public async Task LoginAsync_FailedAttemptBelowThreshold_IncrementsCountReturns401()
    {
        // Arrange: tenant with maxFailedAttempts=5
        await SeedUserAndTenantAsync(maxFailedAttempts: 5);
        var service = CreateService();

        // Act: submit wrong password
        var result = await service.LoginAsync(
            "test@test.local", "WrongPassword!", null, "127.0.0.1", "Chrome", default);

        // Assert: returns 401 with generic error (AC-1: does not reveal remaining attempts)
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Be("Invalid email or password.");
        result.Error.Should().NotContain("attempt");
        result.Error.Should().NotContain("remaining");

        // Verify FailedLoginCount was incremented
        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.FailedLoginCount.Should().Be(1);
        user.LockedUntil.Should().BeNull(); // Not yet locked
    }

    [Fact]
    public async Task LoginAsync_MultipleFailsBelowThreshold_IncrementsCumulatively()
    {
        // Arrange: tenant with maxFailedAttempts=5
        await SeedUserAndTenantAsync(maxFailedAttempts: 5);

        // Act: fail 3 times
        for (int i = 0; i < 3; i++)
        {
            var service = CreateService();
            await service.LoginAsync(
                "test@test.local", "WrongPassword!", null, "127.0.0.1", "Chrome", default);
        }

        // Assert: count is 3, not locked
        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.FailedLoginCount.Should().Be(3);
        user.LockedUntil.Should().BeNull();
    }

    #endregion

    #region AC-2: Account Locked on Reaching Threshold

    [Fact]
    public async Task LoginAsync_ReachesMaxFailedAttempts_LocksAccount()
    {
        // Arrange: tenant with maxFailedAttempts=3, lockoutDurationMinutes=10
        await SeedUserAndTenantAsync(maxFailedAttempts: 3, lockoutDurationMinutes: 10);

        // Act: fail 3 times
        Result<Application.Features.Auth.DTOs.LoginResponse>? lastResult = null;
        for (int i = 0; i < 3; i++)
        {
            var service = CreateService();
            lastResult = await service.LoginAsync(
                "test@test.local", "WrongPassword!", null, "10.0.0.1", "Chrome", default);
        }

        // Assert: 401 with generic error (AC-1: same message as below-threshold)
        lastResult!.IsFailure.Should().BeTrue();
        lastResult.StatusCode.Should().Be(401);

        // Verify account is locked
        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.FailedLoginCount.Should().Be(3);
        user.LockedUntil.Should().NotBeNull();
        user.LockedUntil!.Value.Should().BeAfter(DateTime.UtcNow);
        user.LockedUntil!.Value.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(10), TimeSpan.FromMinutes(1));

        // Verify account_locked audit event exists
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "account_locked" && a.UserId == _userId)
            .Should().BeTrue();

        // Verify lockout notification was enqueued via Hangfire
        _backgroundJobClient.Received().Create(
            Arg.Any<Hangfire.Common.Job>(),
            Arg.Any<Hangfire.States.IState>());
    }

    #endregion

    #region AC-3: Locked User Rejected Even With Correct Credentials

    [Fact]
    public async Task LoginAsync_LockedAccount_RejectsCorrectPassword()
    {
        // Arrange: Lock the account
        await SeedUserAndTenantAsync(maxFailedAttempts: 3, lockoutDurationMinutes: 15);
        await LockUserAsync();
        var service = CreateService();

        // Act: attempt login with correct credentials while locked
        var result = await service.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);

        // Assert: 401 with lockout message (AC-3: correct credentials do NOT bypass lockout)
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Contain("Account temporarily locked");
        result.Error.Should().Contain("Try again later");
    }

    [Fact]
    public async Task LoginAsync_LockedAccount_DoesNotCheckPassword()
    {
        // Arrange: Lock the account, password remains valid
        await SeedUserAndTenantAsync(maxFailedAttempts: 3, lockoutDurationMinutes: 15);
        await LockUserAsync();
        var service = CreateService();

        // Act: attempt login with wrong password while locked
        var result = await service.LoginAsync(
            "test@test.local", "TotallyWrongPassword!", null, "127.0.0.1", "Chrome", default);

        // Assert: same lockout message (password not checked = same response)
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Contain("Account temporarily locked");

        // Verify FailedLoginCount was NOT incremented (password was not checked)
        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.FailedLoginCount.Should().Be(3); // Same as when locked
    }

    #endregion

    #region AC-4: Lockout Expires, Login Succeeds

    [Fact]
    public async Task LoginAsync_ExpiredLockout_ClearsStateAndProcessesLogin()
    {
        // Arrange: Set lockout that has already expired
        await SeedUserAndTenantAsync(maxFailedAttempts: 5);
        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.LockedUntil = DateTime.UtcNow.AddMinutes(-1); // Expired 1 minute ago
            user.FailedLoginCount = 5;
            user.MfaFailedAttemptCount = 2;
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: login with correct credentials after lockout expired
        var result = await service.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);

        // Assert: login succeeds (AC-4)
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();

        // Verify counters cleared
        using var db2 = CreateDbContext();
        var updatedUser = await db2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        updatedUser.FailedLoginCount.Should().Be(0);
        updatedUser.LockedUntil.Should().BeNull();

        // Verify account_unlocked_by_timeout audit event
        db2.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "account_unlocked_by_timeout" && a.UserId == _userId)
            .Should().BeTrue();
    }

    #endregion

    #region AC-5: Admin Unlock

    [Fact]
    public async Task UnlockUserAsync_AdminUnlocksLockedAccount_Success()
    {
        // Arrange: Lock the account, admin has membership in same tenant
        await SeedUserAndTenantAsync(maxFailedAttempts: 3);
        await LockUserAsync();

        // Add admin's tenant membership
        using (var db = CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = _adminUserId,
                Email = "admin@test.local",
                DisplayName = "Admin User",
                IsActive = true,
            });
            var adminMembership = new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _adminUserId,
                TenantId = _tenantId,
                Status = UserTenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
            };
            db.UserTenants.Add(adminMembership);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: admin unlocks the user
        var result = await service.UnlockUserAsync(_userId, _tenantId, _adminUserId, default);

        // Assert: success
        result.IsSuccess.Should().BeTrue();

        // Verify lockout state cleared (AC-5: locked_until=null, failed_login_count=0)
        using var db2 = CreateDbContext();
        var user = await db2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.LockedUntil.Should().BeNull();
        user.FailedLoginCount.Should().Be(0);
        user.MfaFailedAttemptCount.Should().Be(0);

        // Verify account_unlocked_by_admin audit event
        db2.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "account_unlocked_by_admin" && a.UserId == _userId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task UnlockUserAsync_AdminUnlocksAccount_UserCanLoginImmediately()
    {
        // Arrange: Lock the account, then admin unlocks
        await SeedUserAndTenantAsync(maxFailedAttempts: 3, lockoutDurationMinutes: 60);
        await LockUserAsync();

        // Admin membership
        using (var db = CreateDbContext())
        {
            db.UserTenants.Add(new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _adminUserId,
                TenantId = _tenantId,
                Status = UserTenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var unlockService = CreateService();
        await unlockService.UnlockUserAsync(_userId, _tenantId, _adminUserId, default);

        // Act: user tries to login after unlock
        var loginService = CreateService();
        var result = await loginService.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);

        // Assert: login succeeds immediately
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region BR-3: Cross-Tenant Admin Unlock Block

    [Fact]
    public async Task UnlockUserAsync_CrossTenantAdmin_Rejected()
    {
        // Arrange: User is in tenant A, admin tries to unlock from tenant B (no membership in tenant B for user)
        await SeedUserAndTenantAsync(maxFailedAttempts: 3);
        await LockUserAsync();

        // Add tenant B (user has NO membership in tenant B)
        using (var db = CreateDbContext())
        {
            db.Tenants.Add(new Tenant
            {
                Id = _tenantBId,
                Name = "Tenant B",
                Subdomain = "tenantb",
                Status = TenantStatus.Active,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: admin tries to unlock user via tenant B (user has no membership there)
        var result = await service.UnlockUserAsync(_userId, _tenantBId, _adminUserId, default);

        // Assert: rejected with 403
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("does not have a membership");
    }

    #endregion

    #region AC-6: Successful Login Below Threshold Resets Count

    [Fact]
    public async Task LoginAsync_SuccessAfterFailedAttempts_ResetsFailedCount()
    {
        // Arrange: fail 3 times, then succeed
        await SeedUserAndTenantAsync(maxFailedAttempts: 5);

        for (int i = 0; i < 3; i++)
        {
            var failService = CreateService();
            await failService.LoginAsync(
                "test@test.local", "WrongPassword!", null, "127.0.0.1", "Chrome", default);
        }

        // Verify count is 3
        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.FailedLoginCount.Should().Be(3);
        }

        // Act: succeed with correct password
        var successService = CreateService();
        var result = await successService.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);

        // Assert: login succeeds, count is reset to 0 (AC-6)
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();

        using var db2 = CreateDbContext();
        var updatedUser = await db2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        updatedUser.FailedLoginCount.Should().Be(0);
        updatedUser.LockedUntil.Should().BeNull();
    }

    #endregion

    #region Progressive Lockout (FR-9)

    [Fact]
    public async Task LoginAsync_ProgressiveLockout_DoublesLockoutDuration()
    {
        // Arrange: tenant with progressive lockout enabled, base 10 minutes
        await SeedUserAndTenantAsync(
            maxFailedAttempts: 3,
            lockoutDurationMinutes: 10,
            progressiveLockoutEnabled: true);

        // Simulate that user already had 2 recent lockout cycles
        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.LockoutCount = 2;
            user.LastLockoutAt = DateTime.UtcNow.AddMinutes(-30); // Within 24h window
            await db.SaveChangesAsync();
        }

        // Act: trigger another lockout
        for (int i = 0; i < 3; i++)
        {
            var service = CreateService();
            await service.LoginAsync(
                "test@test.local", "WrongPassword!", null, "127.0.0.1", "Chrome", default);
        }

        // Assert: lockout duration should be 10 * 4 = 40 minutes (2 doublings from 2 prior cycles)
        using var db2 = CreateDbContext();
        var lockedUser = await db2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        lockedUser.LockedUntil.Should().NotBeNull();
        // 40 minutes from now (with some tolerance)
        var expectedLockout = DateTime.UtcNow.AddMinutes(40);
        lockedUser.LockedUntil!.Value.Should().BeCloseTo(expectedLockout, TimeSpan.FromMinutes(2));
        lockedUser.LockoutCount.Should().Be(3); // Incremented from 2 to 3
    }

    [Fact]
    public async Task LoginAsync_ProgressiveLockoutDisabled_UsesBaseDuration()
    {
        // Arrange: tenant with progressive lockout DISABLED
        await SeedUserAndTenantAsync(
            maxFailedAttempts: 3,
            lockoutDurationMinutes: 10,
            progressiveLockoutEnabled: false);

        // Simulate prior lockout cycles
        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.LockoutCount = 5;
            user.LastLockoutAt = DateTime.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync();
        }

        // Act: trigger lockout
        for (int i = 0; i < 3; i++)
        {
            var service = CreateService();
            await service.LoginAsync(
                "test@test.local", "WrongPassword!", null, "127.0.0.1", "Chrome", default);
        }

        // Assert: lockout duration remains at base 10 minutes (no doubling)
        using var db2 = CreateDbContext();
        var lockedUser = await db2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        lockedUser.LockedUntil.Should().NotBeNull();
        var expectedLockout = DateTime.UtcNow.AddMinutes(10);
        lockedUser.LockedUntil!.Value.Should().BeCloseTo(expectedLockout, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task LoginAsync_ProgressiveLockout_ResetAfter24Hours()
    {
        // Arrange: progressive lockout enabled, but last lockout was > 24h ago
        await SeedUserAndTenantAsync(
            maxFailedAttempts: 3,
            lockoutDurationMinutes: 10,
            progressiveLockoutEnabled: true);

        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.LockoutCount = 5;
            user.LastLockoutAt = DateTime.UtcNow.AddHours(-25); // Older than 24h
            await db.SaveChangesAsync();
        }

        // Act: trigger lockout
        for (int i = 0; i < 3; i++)
        {
            var service = CreateService();
            await service.LoginAsync(
                "test@test.local", "WrongPassword!", null, "127.0.0.1", "Chrome", default);
        }

        // Assert: lockout duration is base (10 min) since the 24h window expired
        using var db2 = CreateDbContext();
        var lockedUser = await db2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        lockedUser.LockedUntil.Should().NotBeNull();
        var expectedLockout = DateTime.UtcNow.AddMinutes(10);
        lockedUser.LockedUntil!.Value.Should().BeCloseTo(expectedLockout, TimeSpan.FromMinutes(2));
    }

    #endregion

    #region FR-10: MFA Failures Count Toward Lockout

    [Fact]
    public async Task VerifyMfaLoginAsync_MfaFailuresCountTowardLockout()
    {
        // Arrange: user with MFA enabled, tenant maxFailedAttempts=3
        await SeedUserAndTenantAsync(maxFailedAttempts: 3, lockoutDurationMinutes: 15);
        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.MfaEnabled = true;
            user.MfaSecret = "TESTSECRET123456"; // Dummy secret
            await db.SaveChangesAsync();
        }

        var totpService = Substitute.For<ITotpService>();
        totpService.ValidateCode(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        // Act: fail MFA 3 times
        for (int i = 0; i < 3; i++)
        {
            var service = CreateServiceWithTotp(totpService);
            await service.VerifyMfaLoginAsync(
                "test@test.local", "000000", "127.0.0.1", "Chrome", default);
        }

        // Assert: account is locked due to MFA failures counting toward FailedLoginCount
        using var db2 = CreateDbContext();
        var lockedUser = await db2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        lockedUser.FailedLoginCount.Should().Be(3);
        lockedUser.MfaFailedAttemptCount.Should().Be(3);
        lockedUser.LockedUntil.Should().NotBeNull();
        lockedUser.LockedUntil!.Value.Should().BeAfter(DateTime.UtcNow);

        // Verify account_locked audit event
        db2.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "account_locked" && a.UserId == _userId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task VerifyMfaLoginAsync_LockedAccount_RejectsValidMfa()
    {
        // Arrange: Lock the user, then try valid MFA
        await SeedUserAndTenantAsync(maxFailedAttempts: 3, lockoutDurationMinutes: 60);
        await LockUserAsync();

        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.MfaEnabled = true;
            user.MfaSecret = "TESTSECRET123456";
            await db.SaveChangesAsync();
        }

        var totpService = Substitute.For<ITotpService>();
        totpService.ValidateCode(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var service = CreateServiceWithTotp(totpService);

        // Act: valid MFA while locked
        var result = await service.VerifyMfaLoginAsync(
            "test@test.local", "123456", "127.0.0.1", "Chrome", default);

        // Assert: still rejected because account is locked
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Contain("Account temporarily locked");
    }

    #endregion

    #region BR-2: Password Reset Clears Lockout

    [Fact]
    public async Task ResetPasswordAsync_ClearsLockout()
    {
        // Arrange: Lock the account
        await SeedUserAndTenantAsync(maxFailedAttempts: 3);
        await LockUserAsync();

        // Set progressive lockout state
        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.LockoutCount = 3;
            user.LastLockoutAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: reset password (BR-2)
        var result = await service.ResetPasswordAsync(
            "test@test.local", "valid-token", "NewPassword456!", default);

        // Assert: success, lockout cleared
        result.IsSuccess.Should().BeTrue();

        using var db2 = CreateDbContext();
        var user2 = await db2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user2.FailedLoginCount.Should().Be(0);
        user2.LockedUntil.Should().BeNull();
        user2.MfaFailedAttemptCount.Should().Be(0);
        user2.LockoutCount.Should().Be(0);
        user2.LastLockoutAt.Should().BeNull();
    }

    #endregion

    #region Lockout Policy Configuration (FR-3, BR-5)

    [Fact]
    public async Task GetTenantAuthSettingsAsync_IncludesLockoutPolicyFields()
    {
        await SeedUserAndTenantAsync(
            maxFailedAttempts: 7,
            lockoutDurationMinutes: 30,
            progressiveLockoutEnabled: true);
        var service = CreateService();

        var result = await service.GetTenantAuthSettingsAsync(_tenantId, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MaxFailedAttempts.Should().Be(7);
        result.Value.LockoutDurationMinutes.Should().Be(30);
        result.Value.ProgressiveLockoutEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateTenantAuthSettingsAsync_UpdatesLockoutPolicyFields()
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var request = new Application.Features.Auth.DTOs.TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            MaxFailedAttempts = 8,
            LockoutDurationMinutes = 45,
            ProgressiveLockoutEnabled = true,
        };

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantId, request, default);

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == _tenantId);
        tenant.MaxFailedAttempts.Should().Be(8);
        tenant.LockoutDurationMinutes.Should().Be(45);
        tenant.ProgressiveLockoutEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(2)]  // Below BR-5 minimum of 3
    [InlineData(11)] // Above BR-5 maximum of 10
    public async Task UpdateTenantAuthSettingsAsync_MaxFailedAttemptsOutOfRange_Fails(int maxAttempts)
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var request = new Application.Features.Auth.DTOs.TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            MaxFailedAttempts = maxAttempts,
        };

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantId, request, default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("3").And.Contain("10");
    }

    [Theory]
    [InlineData(4)]  // Below BR-5 minimum of 5
    [InlineData(61)] // Above BR-5 maximum of 60
    public async Task UpdateTenantAuthSettingsAsync_LockoutDurationOutOfRange_Fails(int duration)
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var request = new Application.Features.Auth.DTOs.TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            LockoutDurationMinutes = duration,
        };

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantId, request, default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("5").And.Contain("60");
    }

    [Theory]
    [InlineData(3)]  // BR-5 lower bound
    [InlineData(10)] // BR-5 upper bound
    public async Task UpdateTenantAuthSettingsAsync_MaxFailedAttemptsAtBounds_Succeeds(int maxAttempts)
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var request = new Application.Features.Auth.DTOs.TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            MaxFailedAttempts = maxAttempts,
        };

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantId, request, default);
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(5)]  // BR-5 lower bound
    [InlineData(60)] // BR-5 upper bound
    public async Task UpdateTenantAuthSettingsAsync_LockoutDurationAtBounds_Succeeds(int duration)
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var request = new Application.Features.Auth.DTOs.TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            LockoutDurationMinutes = duration,
        };

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantId, request, default);
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Audit Events

    [Fact]
    public async Task LoginAsync_FailedLogin_WritesLoginFailureAudit()
    {
        await SeedUserAndTenantAsync(maxFailedAttempts: 5);
        var service = CreateService();

        await service.LoginAsync(
            "test@test.local", "WrongPassword!", null, "10.0.0.1", "TestAgent", default);

        using var db = CreateDbContext();
        var auditLog = await db.AuditLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.EventType == "login_failure" && a.UserId == _userId);

        auditLog.Should().NotBeNull();
        auditLog!.IpAddress.Should().Be("10.0.0.1");
        auditLog.UserAgent.Should().Be("TestAgent");
        auditLog.Detail.Should().Contain("attemptCount");
    }

    #endregion

    #region BR-7: Lockout Does Not Revoke Existing Refresh Tokens

    [Fact]
    public async Task LoginAsync_AccountLocked_DoesNotRevokeExistingRefreshTokens()
    {
        // Arrange: user has an active session
        await SeedUserAndTenantAsync(maxFailedAttempts: 3);
        var existingSessionId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = existingSessionId,
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "existing-session-hash",
                IssuedAt = DateTime.UtcNow.AddHours(-1),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Act: trigger lockout
        for (int i = 0; i < 3; i++)
        {
            var service = CreateService();
            await service.LoginAsync(
                "test@test.local", "WrongPassword!", null, "127.0.0.1", "Chrome", default);
        }

        // Assert: existing refresh token is still valid (BR-7)
        using var db2 = CreateDbContext();
        var token = await db2.RefreshTokens.IgnoreQueryFilters()
            .FirstAsync(rt => rt.Id == existingSessionId);
        token.RevokedAt.Should().BeNull();
    }

    #endregion

    #region Helpers

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
            _backgroundJobClient);
    }

    private AuthService CreateServiceWithTotp(ITotpService totpService)
    {
        return new AuthService(
            CreateDbContext(),
            _jwtService,
            _tenantContext,
            totpService,
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            _backgroundJobClient);
    }

    private AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task SeedUserAndTenantAsync(
        int maxFailedAttempts = 5,
        int lockoutDurationMinutes = 15,
        bool progressiveLockoutEnabled = false)
    {
        using var db = CreateDbContext();

        // Skip seeding if already seeded
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == _userId))
            return;

        var user = new User
        {
            Id = _userId,
            Email = "test@test.local",
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var tenant = new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Subdomain = "testco",
            Status = TenantStatus.Active,
            MaxFailedAttempts = maxFailedAttempts,
            LockoutDurationMinutes = lockoutDurationMinutes,
            ProgressiveLockoutEnabled = progressiveLockoutEnabled,
            CreatedAt = DateTime.UtcNow,
        };

        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = "Employee",
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        db.Tenants.Add(tenant);
        db.Roles.Add(role);

        var membership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        db.UserTenants.Add(membership);
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "unit-test",
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Locks the user account with standard locked state.
    /// </summary>
    private async Task LockUserAsync()
    {
        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.FailedLoginCount = 3;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(60); // Locked for 60 minutes
        await db.SaveChangesAsync();
    }

    #endregion
}
