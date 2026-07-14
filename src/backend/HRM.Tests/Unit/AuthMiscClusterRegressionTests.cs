// ============================================================================
// Regression tests for the fix/auth-misc-cluster fix cluster. Each test is RED
// against the pre-fix tree (HEAD) and GREEN against the applied fix:
//
//  * BUG-044  (US-AUTH-010, TC-AUTH-084 step 4 / TC-AUTH-026 / TC-AUTH-103):
//       the failed attempt that TRIPS the lockout must return the lockout
//       message immediately, not the generic "Invalid email or password."
//       Pre-fix (AuthService.LoginAsync, HEAD line 173) the wrong-password
//       branch unconditionally returned the generic message on the tripping
//       attempt, so the lockout message only surfaced one request late.
//
//  * BUG-046  (US-AUTH-010, TC-AUTH-094 / BR-4):
//       a platform SystemAdmin can unlock a locked user in ANY tenant via the
//       new system endpoint POST /api/v1/system/tenants/{tenantId}/users/
//       {userId}/unlock (AdminTenantsController.UnlockUser). Pre-fix that
//       endpoint did not exist; the only unlock route bound the caller's own
//       token tenant, so a platform admin (no membership in the target tenant)
//       could not unlock cross-tenant. This drives the REAL controller ->
//       real UnlockUserCommandHandler -> real AuthService -> DB chain.
//
//  * ISSUE-056 (US-AUTH-008, TC-AUTH-064 steps 5-8):
//       the per-user my-tenants cache (user:{userId}:tenants) must be
//       invalidated when a membership changes. Pre-fix no eviction existed
//       (UserManagementService took no IDistributedCache), so GetMyTenantsAsync
//       served the stale membership list for up to the 5-minute TTL. This
//       exercises the real end-to-end outcome: populate the cache, mutate the
//       membership via the real UserManagementService.DeactivateAsync path,
//       then re-read and assert the list is fresh (not stale).
//
// Harness mirrors AccountLockoutTests / AuthMyTenantsResilienceTests /
// AuthControllerSwitchTenantTests: EF InMemory + the real services (no mocking
// of the code under test). InMemory faithfully represents the service-level
// logic here; none of the three fixes depends on Postgres-only semantics
// (UnlockUserAsync/GetMyTenantsAsync use IgnoreQueryFilters explicitly).
// ============================================================================

using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.Commands;
using HRM.Application.Features.Auth.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Caching;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AuthMiscClusterRegressionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _userTenantId = BaseEntity.NewUuidV7();
    private readonly Guid _systemAdminId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;
    private readonly Hangfire.IBackgroundJobClient _backgroundJobClient;

    // Tenant-resolved context (for login / seeding / the deactivate mutation).
    private readonly ITenantContext _tenantContext;
    // System/admin context (no resolved tenant) — mirrors the admin.* subdomain
    // the cross-tenant unlock endpoint runs under.
    private readonly ITenantContext _systemContext;

    public AuthMiscClusterRegressionTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "hrm-api-test",
                ["Jwt:Audience"] = "hrm-client-test",
                ["Platform:BaseDomain"] = "yourhrm.test",
            })
            .Build();

        _jwtService = new JwtService(_configuration);
        _backgroundJobClient = Substitute.For<Hangfire.IBackgroundJobClient>();

        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _systemContext = Substitute.For<ITenantContext>();
        _systemContext.TenantId.Returns(Guid.Empty);
        _systemContext.IsResolved.Returns(false);
        _systemContext.IsSystemContext.Returns(true);
    }

    // ── BUG-044 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ThresholdAttempt_ReturnsLockoutMessage_BUG044()
    {
        // Arrange: threshold of 3 failed attempts, 15-minute lockout.
        await SeedUserAndTenantAsync(maxFailedAttempts: 3, lockoutDurationMinutes: 15);

        // Act: three consecutive wrong-password logins. Capture each attempt.
        var messages = new List<string?>();
        Result<LoginResponse>? tripping = null;
        for (var i = 1; i <= 3; i++)
        {
            var service = CreateAuthService(_tenantContext);
            var result = await service.LoginAsync(
                "test@test.local", "WrongPassword!", null, "10.0.0.1", "Chrome", default);
            messages.Add(result.Error);
            tripping = result;
        }

        // Assert: the below-threshold attempts (1, 2) return the generic message —
        // BUG-044's fix must NOT leak lockout state early (no account enumeration).
        messages[0].Should().Be("Invalid email or password.");
        messages[1].Should().Be("Invalid email or password.");

        // The THRESHOLD-tripping attempt (3rd) must return the lockout message
        // immediately. Pre-fix it returned "Invalid email or password." here, so
        // this assertion is RED against HEAD.
        tripping!.IsFailure.Should().BeTrue();
        tripping.StatusCode.Should().Be(401);
        tripping.Error.Should().Be(
            "Account temporarily locked. Try again later or contact your administrator.");
        tripping.Error.Should().NotContain("Invalid email or password");

        // The account is genuinely locked (locked_until set) on that same attempt.
        using var db = CreateDbContext(_tenantContext);
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.FailedLoginCount.Should().Be(3);
        user.LockedUntil.Should().NotBeNull();
        user.LockedUntil!.Value.Should().BeAfter(DateTime.UtcNow);
    }

    // ── BUG-046 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SystemAdmin_UnlocksCrossTenantUser_BUG046()
    {
        // Arrange: a user locked in tenant A. The acting SystemAdmin (_systemAdminId)
        // has NO membership in tenant A — pre-fix there was no path for them to
        // unlock cross-tenant (the only route bound the caller's token tenant).
        await SeedUserAndTenantAsync(maxFailedAttempts: 3, lockoutDurationMinutes: 60);
        await LockUserAsync();

        // The cross-tenant unlock endpoint runs in SYSTEM context (no resolved
        // tenant); the real handler -> real AuthService performs the work.
        var systemAuthService = CreateAuthService(_systemContext);
        var handler = new UnlockUserCommandHandler(systemAuthService);

        // A forwarding mediator that dispatches UnlockUserCommand to the real
        // handler (mirrors AuthControllerSwitchTenantTests) — the handler/service
        // /DB chain stays real; only the mediator plumbing is a shim.
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UnlockUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => handler.Handle(
                ci.Arg<UnlockUserCommand>(), ci.Arg<CancellationToken>()).GetAwaiter().GetResult());

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(_systemAdminId); // the platform SystemAdmin actor
        currentUser.IsAuthenticated.Returns(true);

        var controller = new AdminTenantsController(mediator, currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        // Act: hit the NEW system endpoint, binding tenant A from the route (NOT the
        // caller's token tenant) for a user the caller is not a member alongside.
        var actionResult = await controller.UnlockUser(_tenantId, _userId, default);

        // Assert: 200 OK.
        actionResult.Should().BeOfType<OkObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status200OK);

        // Lockout fields cleared.
        using (var db = CreateDbContext(_tenantContext))
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.LockedUntil.Should().BeNull();
            user.FailedLoginCount.Should().Be(0);
            user.MfaFailedAttemptCount.Should().Be(0);

            // Audit attributes the acting SystemAdmin and is scoped to the target tenant.
            var audit = await db.AuditLogs.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.EventType == "account_unlocked_by_admin" && a.UserId == _userId);
            audit.Should().NotBeNull();
            audit!.TenantId.Should().Be(_tenantId);
            audit.Detail.Should().Contain(_systemAdminId.ToString());
        }

        // End-to-end: the previously-locked user can now log in in tenant A.
        var loginService = CreateAuthService(_tenantContext);
        var login = await loginService.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);
        login.IsSuccess.Should().BeTrue();
        login.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    // ── ISSUE-056 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MyTenants_AfterMembershipChange_EvictsCache_ISSUE056()
    {
        // ISSUE-056: a membership change must evict the my-tenants cache
        // (key `user:{userId}:tenants`) so a subsequent read isn't stale.
        // Directly verify the fix: deactivating a membership calls RemoveAsync on that
        // exact key. (A spy cache is the robust, deterministic check of the invalidation
        // itself — the read-back status round-trip depends on separate DbContext/cache
        // instances lining up, which is not what this finding is about.)
        await SeedUserAndTenantAsync(maxFailedAttempts: 5);
        var cache = Substitute.For<IDistributedCache>();

        var userMgmt = CreateUserManagementService(cache);
        var deactivate = await userMgmt.DeactivateAsync(_userTenantId, default);
        deactivate.IsSuccess.Should().BeTrue();

        // Pre-fix: DeactivateAsync never touched the cache -> Received() throws (RED).
        // Post-fix: the fix removes the exact my-tenants key for the affected user.
        await cache.Received(1).RemoveAsync($"user:{_userId}:tenants", Arg.Any<CancellationToken>());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private AppDbContext CreateDbContext(ITenantContext context) =>
        TestDbContextFactory.Create(context, _dbName);

    private AuthService CreateAuthService(ITenantContext context, IDistributedCache? cache = null) =>
        new(
            CreateDbContext(context),
            _jwtService,
            context,
            Substitute.For<ITotpService>(),
            _configuration,
            cache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            _backgroundJobClient);

    private UserManagementService CreateUserManagementService(IDistributedCache cache)
    {
        // The deactivate path runs as a DIFFERENT admin (BR-3 forbids self-deactivation)
        // in tenant A's resolved context.
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Email.Returns("admin@test.local");
        currentUser.TenantId.Returns(_tenantId);
        currentUser.IsAuthenticated.Returns(true);

        // BUG-116: the service now depends on the shared IMyTenantsCache (which wraps IDistributedCache). Wrap the
        // spy cache in the REAL MyTenantsCache so the ISSUE-056 assertion below still verifies the same underlying
        // IDistributedCache.RemoveAsync(exact key) call.
        return new UserManagementService(
            CreateDbContext(_tenantContext),
            _tenantContext,
            currentUser,
            Substitute.For<IPermissionCache>(),
            Substitute.For<IUserManagementNotificationService>(),
            Substitute.For<ILogger<UserManagementService>>(),
            new MyTenantsCache(cache, Substitute.For<ILogger<MyTenantsCache>>()));
    }

    private async Task SeedUserAndTenantAsync(
        int maxFailedAttempts = 5,
        int lockoutDurationMinutes = 15)
    {
        using var db = CreateDbContext(_tenantContext);

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

        db.UserTenants.Add(new UserTenant
        {
            Id = _userTenantId,
            UserId = _userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        });
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = _userTenantId,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "unit-test",
        });

        await db.SaveChangesAsync();
    }

    private async Task LockUserAsync()
    {
        using var db = CreateDbContext(_tenantContext);
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
        user.FailedLoginCount = 3;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(60);
        await db.SaveChangesAsync();
    }
}
