using FluentAssertions;
using Hangfire;
using HRM.Application.Common.Helpers;
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
using Xunit;

namespace HRM.Tests.Unit;

/// <summary>
/// ISSUE-062 (US-AUTH-010 FR-7): lockout and unlock must be visible to the PLATFORM operator, not only
/// inside the tenant they happened in.
///
/// <para>The platform view is a SECOND <c>audit_logs</c> row carrying <c>TenantId = null</c> — the
/// pre-existing system-scoped convention (<c>AuditLog</c> documents "there is intentionally no second audit
/// table", <c>audit_logs</c> is the one entity whose global query filter keeps a <c>TenantId == null</c> arm,
/// and <c>PlatformMonitoringService</c> already writes its rows that way). FR-7 wants BOTH views, so the
/// duplication is the requirement.</para>
///
/// <para>Each test asserts the ACTION and that EXACTLY TWO rows exist for it — one tenant-scoped, one
/// system-scoped. A count assertion is what catches the regression that matters: dropping either row (or
/// writing two rows into the same scope) would still leave a naive "an account_locked row exists" check
/// green.</para>
/// </summary>
public sealed class AuthPlatformLockoutAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public AuthPlatformLockoutAuditTests()
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

    // ══════════════════════════════════════════════════════════════
    //  FR-7: account_locked
    // ══════════════════════════════════════════════════════════════

    [Fact]
    [Trait("TC", "TC-AUTH-062-01")]
    public async Task Lockout_writes_both_a_tenant_row_and_a_platform_row_Issue062()
    {
        await SeedUserAndTenantAsync(maxFailedAttempts: 3);

        for (var i = 0; i < 3; i++)
        {
            var service = CreateService();
            await service.LoginAsync("test@test.local", "WrongPassword!", null, "10.0.0.1", "Chrome", default);
        }

        using var db = CreateDbContext();
        var rows = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == PlatformVisibleAuditEvents.AccountLocked && a.UserId == _userId)
            .ToListAsync();

        rows.Should().HaveCount(2,
            "FR-7 requires BOTH a tenant-scoped audit row and a platform-visible system-scoped one");
        rows.Should().ContainSingle(a => a.TenantId == _tenantId, "the tenant keeps its own lockout trail");
        rows.Should().ContainSingle(a => a.TenantId == null, "the platform operator must see it cross-tenant");

        var platformRow = rows.Single(a => a.TenantId == null);
        platformRow.Action.Should().Be(PlatformVisibleAuditEvents.AccountLocked);
        platformRow.ResourceType.Should().Be("Tenant");
        platformRow.ResourceId.Should().Be(_tenantId.ToString(),
            "the platform row must still say WHICH tenant the lockout happened in");
        platformRow.UserId.Should().Be(_userId);
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-7: account_unlocked_by_admin
    // ══════════════════════════════════════════════════════════════

    [Fact]
    [Trait("TC", "TC-AUTH-062-02")]
    public async Task Admin_unlock_writes_both_a_tenant_row_and_a_platform_row_Issue062()
    {
        await SeedUserAndTenantAsync();
        await LockUserAsync();
        await SeedAdminAsync();

        var result = await CreateService().UnlockUserAsync(_userId, _tenantId, _adminUserId, default);
        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var rows = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == PlatformVisibleAuditEvents.AccountUnlockedByAdmin && a.UserId == _userId)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(a => a.TenantId == _tenantId);
        rows.Should().ContainSingle(a => a.TenantId == null);

        var platformRow = rows.Single(a => a.TenantId == null);
        platformRow.Action.Should().Be(PlatformVisibleAuditEvents.AccountUnlockedByAdmin);
        platformRow.ResourceId.Should().Be(_tenantId.ToString());
        // The structured detail (which admin acted) must survive onto the platform copy too.
        platformRow.Detail.Should().Contain(_adminUserId.ToString());
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-7: account_unlocked_by_timeout
    // ══════════════════════════════════════════════════════════════

    [Fact]
    [Trait("TC", "TC-AUTH-062-03")]
    public async Task Timeout_unlock_writes_both_a_tenant_row_and_a_platform_row_Issue062()
    {
        await SeedUserAndTenantAsync();

        // Lockout already expired -> the next login attempt clears it and audits account_unlocked_by_timeout.
        using (var seed = CreateDbContext())
        {
            var user = await seed.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.FailedLoginCount = 3;
            user.LockedUntil = DateTime.UtcNow.AddMinutes(-5);
            await seed.SaveChangesAsync();
        }

        await CreateService().LoginAsync(
            "test@test.local", "Password123!", null, "10.0.0.1", "Chrome", default);

        using var db = CreateDbContext();
        var rows = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == PlatformVisibleAuditEvents.AccountUnlockedByTimeout && a.UserId == _userId)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(a => a.TenantId == _tenantId);
        rows.Should().ContainSingle(a => a.TenantId == null);
        rows.Single(a => a.TenantId == null).Action
            .Should().Be(PlatformVisibleAuditEvents.AccountUnlockedByTimeout);
    }

    // ══════════════════════════════════════════════════════════════
    //  Negative control — the copy is scoped to the FR-7 events only
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// A plain failed login is NOT an FR-7 platform event. If the copy were unconditional, every auth audit
    /// row in the system would be doubled — so this arm is what keeps the whitelist honest.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-AUTH-062-04")]
    public async Task Ordinary_login_failure_writes_only_the_tenant_row_Issue062()
    {
        await SeedUserAndTenantAsync(maxFailedAttempts: 5);

        await CreateService().LoginAsync(
            "test@test.local", "WrongPassword!", null, "10.0.0.1", "Chrome", default);

        using var db = CreateDbContext();
        var rows = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == "login_failure" && a.UserId == _userId)
            .ToListAsync();

        rows.Should().ContainSingle("login_failure is not in the FR-7 platform-visible set");
        rows.Single().TenantId.Should().Be(_tenantId);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private AuthService CreateService() => new(
        CreateDbContext(),
        _jwtService,
        _tenantContext,
        Substitute.For<ITotpService>(),
        _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(),
        _backgroundJobClient);

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private async Task SeedUserAndTenantAsync(int maxFailedAttempts = 5, int lockoutDurationMinutes = 15)
    {
        using var db = CreateDbContext();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == _userId))
            return;

        db.Users.Add(new User
        {
            Id = _userId,
            Email = "test@test.local",
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Subdomain = "testco",
            Status = TenantStatus.Active,
            MaxFailedAttempts = maxFailedAttempts,
            LockoutDurationMinutes = lockoutDurationMinutes,
            CreatedAt = DateTime.UtcNow,
        });

        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = "Employee",
            CreatedAt = DateTime.UtcNow,
        };
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

    private async Task SeedAdminAsync()
    {
        using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Id = _adminUserId,
            Email = "admin@test.local",
            DisplayName = "Admin User",
            IsActive = true,
        });
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

    private async Task LockUserAsync()
    {
        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.FailedLoginCount = 3;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(60);
        await db.SaveChangesAsync();
    }
}
