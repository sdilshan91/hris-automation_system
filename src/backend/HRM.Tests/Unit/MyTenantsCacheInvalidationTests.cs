// ============================================================================
// BUG-116: the my-tenants cache (user:{userId}:tenants — a user's tenant
// memberships + roles, TTL 5 min) must be invalidated on EVERY membership/role
// mutation, via the shared IMyTenantsCache targeting ONE key source
// (MyTenantsCacheKey.For). Pre-fix the entry had no invalidation at several
// sites, so authorization data was served stale for up to the TTL.
//
// These tests cover:
//   1. MyTenantsCache.InvalidateAsync -> IDistributedCache.RemoveAsync(exact key), Received(1).
//   2. MyTenantsCache is fail-soft: a thrown Redis exception is swallowed (never breaks the mutation).
//   3. RoleService.AssignUserRolesAsync (role replace) invalidates the affected user's entry.
//   4. UserManagementService.DeactivateAsync (membership disabled) invalidates the affected user's entry.
// Uses EF Core InMemory + the real services (no mocking of the code under test).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Auth.DTOs;
using HRM.Application.Features.Tenants.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Caching;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace HRM.Tests.Unit;

public sealed class MyTenantsCacheInvalidationTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public MyTenantsCacheInvalidationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    // ── 1. The invalidator removes the exact key ─────────────────────────────

    [Fact]
    public async Task InvalidateAsync_RemovesExactMyTenantsKey()
    {
        var userId = Guid.NewGuid();
        var cache = Substitute.For<IDistributedCache>();
        var sut = new MyTenantsCache(cache, Substitute.For<ILogger<MyTenantsCache>>());

        await sut.InvalidateAsync(userId);

        await cache.Received(1).RemoveAsync(MyTenantsCacheKey.For(userId), Arg.Any<CancellationToken>());
        MyTenantsCacheKey.For(userId).Should().Be($"user:{userId}:tenants");
    }

    // ── 2. Fail-soft: a Redis exception is swallowed ─────────────────────────

    [Fact]
    public async Task InvalidateAsync_WhenCacheThrows_SwallowsException()
    {
        var userId = Guid.NewGuid();
        var cache = Substitute.For<IDistributedCache>();
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("redis down"));
        var sut = new MyTenantsCache(cache, Substitute.For<ILogger<MyTenantsCache>>());

        // Must NOT throw — a cache blip can never break the membership mutation that triggered the eviction.
        var act = async () => await sut.InvalidateAsync(userId);
        await act.Should().NotThrowAsync();
    }

    // ── 3. RoleService role-replace invalidates via IMyTenantsCache ──────────

    [Fact]
    public async Task RoleService_AssignUserRoles_InvalidatesMyTenantsCache()
    {
        var roleId = await SeedCustomRoleAsync("Dev Lead");
        var (userTenantId, userId) = await SeedUserTenantAsync();

        var myTenantsCache = Substitute.For<IMyTenantsCache>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Email.Returns("admin@test.com");
        currentUser.UserId.Returns(Guid.NewGuid());

        var service = new RoleService(
            TestDbContextFactory.Create(_tenantContext, _dbName),
            _tenantContext,
            currentUser,
            new InMemoryPermissionCache(),
            Substitute.For<ILogger<RoleService>>(),
            myTenantsCache);

        var result = await service.AssignUserRolesAsync(userTenantId, new[] { roleId });

        result.IsSuccess.Should().BeTrue();
        await myTenantsCache.Received(1).InvalidateAsync(userId, Arg.Any<CancellationToken>());
    }

    // ── 4. UserManagementService disable invalidates via IMyTenantsCache ─────

    [Fact]
    public async Task UserManagementService_Deactivate_InvalidatesMyTenantsCache()
    {
        var (userTenantId, userId) = await SeedUserTenantAsync();

        var myTenantsCache = Substitute.For<IMyTenantsCache>();
        // BR-3: the deactivating admin must be a DIFFERENT user than the target.
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Email.Returns("admin@test.com");
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.TenantId.Returns(_tenantId);
        currentUser.IsAuthenticated.Returns(true);

        var service = new UserManagementService(
            TestDbContextFactory.Create(_tenantContext, _dbName),
            _tenantContext,
            currentUser,
            Substitute.For<IPermissionCache>(),
            Substitute.For<IUserManagementNotificationService>(),
            Substitute.For<ILogger<UserManagementService>>(),
            myTenantsCache);

        var result = await service.DeactivateAsync(userTenantId);

        result.IsSuccess.Should().BeTrue();
        await myTenantsCache.Received(1).InvalidateAsync(userId, Arg.Any<CancellationToken>());
    }

    // ── 5. UserManagementService role-CHANGE (EditRolesAsync) invalidates ────

    [Fact]
    public async Task UserManagementService_EditRoles_InvalidatesMyTenantsCache()
    {
        var roleId = await SeedCustomRoleAsync("Analyst");
        var (userTenantId, userId) = await SeedUserTenantAsync();

        var myTenantsCache = Substitute.For<IMyTenantsCache>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Email.Returns("admin@test.com");
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.TenantId.Returns(_tenantId);
        currentUser.IsAuthenticated.Returns(true);

        var service = new UserManagementService(
            TestDbContextFactory.Create(_tenantContext, _dbName),
            _tenantContext,
            currentUser,
            Substitute.For<IPermissionCache>(),
            Substitute.For<IUserManagementNotificationService>(),
            Substitute.For<ILogger<UserManagementService>>(),
            myTenantsCache);

        var result = await service.EditRolesAsync(userTenantId, new[] { roleId });

        result.IsSuccess.Should().BeTrue(result.Error);
        await myTenantsCache.Received(1).InvalidateAsync(userId, Arg.Any<CancellationToken>());
    }

    // ── 6. AuthService SSO-JIT new membership invalidates ────────────────────

    [Fact]
    public async Task AuthService_SsoJitProvision_InvalidatesMyTenantsCache()
    {
        // Seed a tenant, an EXISTING global user matched by email, and the default JIT role — but NO membership,
        // so SsoSignInAsync takes the just-in-time membership branch (creates a new UserTenant + SaveChanges +
        // invalidate). The user is matched by email (no oid link yet), reaching the membership-JIT path.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using (var db = TestDbContextFactory.Create(_tenantContext, _dbName))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Subdomain = "ssoco",
                Name = "SSO Co",
                Status = TenantStatus.Active,
                MaxConcurrentSessions = 5,
                CreatedAt = DateTime.UtcNow,
            });
            db.Users.Add(new User
            {
                Id = userId,
                Email = "ssouser@ssoco.com",
                DisplayName = "SSO User",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            db.Roles.Add(new Role
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = "Employee",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "hrm-api-test",
                ["Jwt:Audience"] = "hrm-client-test",
                ["Platform:BaseDomain"] = "yourhrm.test",
            })
            .Build();

        var myTenantsCache = Substitute.For<IMyTenantsCache>();
        var authService = new AuthService(
            TestDbContextFactory.Create(_tenantContext, _dbName),
            new JwtService(config),
            _tenantContext,
            Substitute.For<ITotpService>(),
            config,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>(),
            myTenantsCache: myTenantsCache);

        var identity = new SsoIdentity
        {
            Subdomain = "ssoco",
            TenantId = "entra-tid",
            ObjectId = "oid-123",
            Email = "ssouser@ssoco.com",
            DisplayName = "SSO User",
            JitAllowed = true,
            DefaultRole = "Employee",
            IpAddress = "10.0.0.1",
            UserAgent = "Chrome",
        };

        var result = await authService.SsoSignInAsync(identity);

        result.IsSuccess.Should().BeTrue(result.Error);
        await myTenantsCache.Received(1).InvalidateAsync(userId, Arg.Any<CancellationToken>());
    }

    // ── 7. TenantProvisioningService links existing owner → invalidates ──────

    [Fact]
    public async Task TenantProvisioning_LinksExistingOwner_InvalidatesMyTenantsCache()
    {
        var systemContext = SystemContext();
        var existingUserId = Guid.NewGuid();
        Guid planId;
        using (var db = TestDbContextFactory.Create(systemContext, _dbName))
        {
            // An EXISTING global user whose email is the new tenant's owner — provisioning LINKS them (a new
            // membership), so their cached my-tenants list could be stale and must be invalidated.
            db.Users.Add(new User
            {
                Id = existingUserId,
                Email = "owner@acme.com",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            var plan = new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Name = "Professional",
                Code = $"pro-{Guid.NewGuid():N}"[..12],
                PriceMonthly = 99m,
                TrialDays = 14,
                MaxEmployees = 50,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            db.SubscriptionPlans.Add(plan);
            await db.SaveChangesAsync();
            planId = plan.Id;
        }

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Email.Returns("sysadmin@platform.test");

        var myTenantsCache = Substitute.For<IMyTenantsCache>();
        var service = new TenantProvisioningService(
            TestDbContextFactory.Create(systemContext, _dbName),
            currentUser,
            Substitute.For<ITenantWelcomeEmailService>(),
            new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<TenantProvisioningService>>(),
            myTenantsCache);

        var input = new ProvisionTenantInput("Acme Corp", "acme", planId, "owner@acme.com", "us-east", null);
        var result = await service.ProvisionAsync(input);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.OwnerLinked.Should().BeTrue();
        await myTenantsCache.Received(1).InvalidateAsync(existingUserId, Arg.Any<CancellationToken>());
    }

    // ── 8. TenantDataDeletionService invalidates EACH removed member ─────────

    [Fact]
    public async Task TenantDataDeletion_InvalidatesEachAffectedUser()
    {
        var systemContext = SystemContext();
        var tenantId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using (var db = TestDbContextFactory.Create(systemContext, _dbName))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Subdomain = "dyingco",
                Name = "Dying Co",
                Status = TenantStatus.Terminating, // DeleteTenantDataAsync only proceeds for a Terminating tenant
                CreatedAt = DateTime.UtcNow,
            });
            foreach (var uid in new[] { userA, userB })
            {
                db.Users.Add(new User { Id = uid, Email = $"m-{uid}@dyingco.com", IsActive = true, CreatedAt = DateTime.UtcNow });
                db.UserTenants.Add(new UserTenant
                {
                    Id = BaseEntity.NewUuidV7(),
                    UserId = uid,
                    TenantId = tenantId,
                    Status = UserTenantStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        var myTenantsCache = Substitute.For<IMyTenantsCache>();
        var service = new TenantDataDeletionService(
            TestDbContextFactory.Create(systemContext, _dbName),
            Substitute.For<ILogger<TenantDataDeletionService>>(),
            myTenantsCache);

        var result = await service.DeleteTenantDataAsync(tenantId);

        result.IsSuccess.Should().BeTrue(result.Error);
        // Pins the per-user foreach loop: EACH removed member is invalidated exactly once (a bug that invalidated
        // only the first user would be caught here).
        await myTenantsCache.Received(1).InvalidateAsync(userA, Arg.Any<CancellationToken>());
        await myTenantsCache.Received(1).InvalidateAsync(userB, Arg.Any<CancellationToken>());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private ITenantContext SystemContext()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(Guid.Empty);
        ctx.IsResolved.Returns(false);
        ctx.IsSystemContext.Returns(true);
        return ctx;
    }

    private async Task<Guid> SeedCustomRoleAsync(string name)
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = name,
            IsBuiltIn = false,
            CreatedAt = DateTime.UtcNow,
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private async Task<(Guid userTenantId, Guid userId)> SeedUserTenantAsync()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Email = $"user-{userId}@test.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        var userTenant = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        db.UserTenants.Add(userTenant);
        await db.SaveChangesAsync();
        return (userTenant.Id, userId);
    }

    public void Dispose()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        db.Database.EnsureDeleted();
    }
}
