// ============================================================================
// US-AUTH-012 — per-tenant SSO configuration on TenantAuthSettings, exercised
// against REAL PostgreSQL via Testcontainers (NOT InMemory), driving the real
// AuthService.{Get,Update}TenantAuthSettingsAsync / GetSsoSettingsAsync.
//
// Covers: persistence of the jsonb allow-list + scalar SSO columns (FR-1/FR-2),
// the plan-entitlement gate returning 403 sso_not_entitled (FR-3/AC-2), the
// fail-closed empty-allow-list block (AC-4/BR-3), the JIT-role existence +
// privilege-ceiling guard (AC-5/FR-6/BR-5), the sso_only break-glass precondition
// (AC-7/BR-6), the sso_config_updated audit row with before/after (FR-7), tenant
// isolation of the settings (AC-6/NFR-2), and cache read/invalidation (NFR-1).
//
// The InMemory provider cannot honour jsonb columns or prove the real migration
// applies, so this uses a real Postgres and MigrateAsync (mirrors
// AuthConcurrentLockoutPostgresTests).
// ============================================================================

using FluentAssertions;
using Hangfire;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Auth.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-AUTH-012")]
public sealed class TenantSsoSettingsPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();     // entitled (plan "pro", Sso=true)
    private readonly Guid _tenantB = Guid.NewGuid();     // entitled — isolation counterpart
    private readonly Guid _tenantFree = Guid.NewGuid();  // NOT entitled (plan "free", Sso=false)

    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "hrm-api-test",
            ["Jwt:Audience"] = "hrm-client-test",
            ["Platform:BaseDomain"] = "yourhrm.test",
        })
        .Build();

    // A single shared cache instance so cache-hit / invalidation across service calls is observable.
    private readonly IDistributedCache _sharedCache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private JwtService _jwtService = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _jwtService = new JwtService(_configuration);

        await using var db = Context(_tenantA);
        await db.Database.MigrateAsync();

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Code = "pro", Name = "Pro",
            FeatureFlags = new PlanFeatureFlags { Sso = true },
        });
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Code = "free", Name = "Free",
            FeatureFlags = new PlanFeatureFlags { Sso = false },
        });

        db.Tenants.Add(Tenant("acme", _tenantA, "pro"));
        db.Tenants.Add(Tenant("globex", _tenantB, "pro"));
        db.Tenants.Add(Tenant("initech", _tenantFree, "free"));

        // Roles for tenant A (JIT-role validation): a non-privileged "Employee" plus the privileged admin roles.
        db.Roles.Add(Role(_tenantA, PermissionCatalog.BuiltInRoles.Employee));
        db.Roles.Add(Role(_tenantA, PermissionCatalog.BuiltInRoles.TenantAdmin));
        db.Roles.Add(Role(_tenantA, PermissionCatalog.BuiltInRoles.TenantOwner));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── FR-1/FR-2/FR-7: persist SSO fields + audit ────────────────────────────

    [Fact]
    public async Task Update_EnablesSso_PersistsJsonbFields_AndWritesAuditRow()
    {
        var service = Service(_tenantA);
        var tid = Guid.NewGuid().ToString();

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantA, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            SsoEnabled = true,
            AllowedEntraTenantIds = [tid],
            AllowedEmailDomains = ["Contoso.com"],
            JitEnabled = true,
            JitDefaultRole = "Employee",
            EnforcementMode = "optional",
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verify = Context(_tenantA);
        var t = await verify.Tenants.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == _tenantA);
        t.SsoEnabled.Should().BeTrue();
        t.AllowedEntraTenantIds.Should().ContainSingle().Which.Should().Be(tid);
        t.AllowedEmailDomains.Should().ContainSingle().Which.Should().Be("contoso.com"); // normalized lower-case
        t.JitEnabled.Should().BeTrue();
        t.JitDefaultRole.Should().Be("Employee");
        t.SsoEnforcementMode.Should().Be("optional");

        // FR-7: audit row with before/after, no secret material.
        var audit = await verify.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == _tenantA && a.EventType == "sso_config_updated")
            .ToListAsync();
        audit.Should().ContainSingle();
        audit[0].Detail.Should().NotBeNullOrEmpty();
        audit[0].Detail!.Should().Contain("before").And.Contain("after");
    }

    // ── FR-3/AC-2: plan entitlement gate ──────────────────────────────────────

    [Fact]
    public async Task Update_SsoWriteWithoutEntitlement_Returns403_SsoNotEntitled_AndPersistsNothing()
    {
        var service = Service(_tenantFree);

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantFree, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            SsoEnabled = true,
            AllowedEmailDomains = ["contoso.com"],
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("sso_not_entitled");

        await using var verify = Context(_tenantFree);
        var t = await verify.Tenants.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == _tenantFree);
        t.SsoEnabled.Should().BeFalse("a 403 must not mutate the tenant");
        t.AllowedEmailDomains.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_NonSsoWriteWithoutEntitlement_IsAllowed()
    {
        // A pure MFA/session update on a non-entitled tenant must NOT be gated by the SSO entitlement.
        var service = Service(_tenantFree);

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantFree, new TenantAuthSettingsRequest
        {
            MfaPolicy = "optional",
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-4/BR-3: fail-closed empty allow-list ───────────────────────────────

    [Fact]
    public async Task Update_EnableSsoWithEmptyAllowList_Returns400()
    {
        var service = Service(_tenantA);

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantA, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            SsoEnabled = true, // no tids, no domains, tenant has none stored
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Add at least one trusted directory or email domain");
    }

    // ── AC-5/FR-6/BR-5: JIT default role guard ────────────────────────────────

    [Fact]
    public async Task Update_JitRoleNotInTenant_Returns400()
    {
        var service = Service(_tenantA);

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantA, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            AllowedEmailDomains = ["contoso.com"],
            JitDefaultRole = "Ghost Role",
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("does not exist in this tenant");
    }

    [Fact]
    public async Task Update_JitRolePrivileged_Returns400_EvenIfItExists()
    {
        var service = Service(_tenantA);

        // "Tenant Admin" exists in the tenant but is above the privilege ceiling (BR-5).
        var result = await service.UpdateTenantAuthSettingsAsync(_tenantA, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            AllowedEmailDomains = ["contoso.com"],
            JitDefaultRole = "Tenant Admin",
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("privileged admin or owner role");
    }

    // ── AC-7/BR-6: sso_only break-glass precondition ──────────────────────────

    [Fact]
    public async Task Update_SsoOnlyWithoutLocalBreakGlassAdmin_Returns400_ThenSucceedsOncePresent()
    {
        var service = Service(_tenantB);
        var tid = Guid.NewGuid().ToString();

        var blocked = await service.UpdateTenantAuthSettingsAsync(_tenantB, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            SsoEnabled = true,
            AllowedEntraTenantIds = [tid],
            EnforcementMode = "sso_only",
        }, CancellationToken.None);

        blocked.IsFailure.Should().BeTrue();
        blocked.StatusCode.Should().Be(400);
        blocked.Error.Should().Contain("break-glass");

        // Provision a local (password) Tenant Admin for tenant B → break-glass path preserved.
        await SeedLocalAdminAsync(_tenantB);

        var allowed = await service.UpdateTenantAuthSettingsAsync(_tenantB, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            SsoEnabled = true,
            AllowedEntraTenantIds = [tid],
            EnforcementMode = "sso_only",
        }, CancellationToken.None);

        allowed.IsSuccess.Should().BeTrue();
    }

    // ── AC-6/NFR-2: tenant isolation of the settings ──────────────────────────

    [Fact]
    public async Task Settings_AreIsolatedPerTenant()
    {
        var tidA = Guid.NewGuid().ToString();
        await Service(_tenantA).UpdateTenantAuthSettingsAsync(_tenantA, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            SsoEnabled = true,
            AllowedEntraTenantIds = [tidA],
        }, CancellationToken.None);

        // A different tenant sees only its own (default, disabled) SSO config — never tenant A's.
        var bSnapshot = (await Service(_tenantB).GetSsoSettingsAsync(_tenantB, CancellationToken.None)).Value!;
        bSnapshot.SsoEnabled.Should().BeFalse();
        bSnapshot.AllowedEntraTenantIds.Should().NotContain(tidA);

        var bResponse = (await Service(_tenantB).GetTenantAuthSettingsAsync(_tenantB, CancellationToken.None)).Value!;
        bResponse.AllowedEntraTenantIds.Should().NotContain(tidA);
    }

    // ── NFR-1: cache read + invalidation on write ─────────────────────────────

    [Fact]
    public async Task GetSsoSettings_IsCached_AndInvalidatedOnUpdate()
    {
        var service = Service(_tenantA);

        // Seed some SSO state through the service (also warms nothing — update invalidates).
        await service.UpdateTenantAuthSettingsAsync(_tenantA, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            SsoEnabled = true,
            AllowedEmailDomains = ["contoso.com"],
        }, CancellationToken.None);

        // Read → populates the shared cache.
        var first = (await service.GetSsoSettingsAsync(_tenantA, CancellationToken.None)).Value!;
        first.SsoEnabled.Should().BeTrue();

        // Mutate the row directly behind the cache (no invalidation) — a cache HIT must still return the old value.
        await using (var raw = Context(_tenantA))
        {
            var t = await raw.Tenants.IgnoreQueryFilters().FirstAsync(x => x.Id == _tenantA);
            t.SsoEnabled = false;
            await raw.SaveChangesAsync();
        }

        var stale = (await service.GetSsoSettingsAsync(_tenantA, CancellationToken.None)).Value!;
        stale.SsoEnabled.Should().BeTrue("the cached snapshot must be served until an SSO write invalidates it");

        // A write through the service invalidates the cache → the next read is fresh from the DB.
        await service.UpdateTenantAuthSettingsAsync(_tenantA, new TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            AllowedEmailDomains = ["fabrikam.com"],
        }, CancellationToken.None);

        var fresh = (await service.GetSsoSettingsAsync(_tenantA, CancellationToken.None)).Value!;
        fresh.AllowedEmailDomains.Should().ContainSingle().Which.Should().Be("fabrikam.com");
        fresh.SsoEnabled.Should().BeFalse("the direct mutation is now visible after the cache was invalidated");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Tenant Tenant(string subdomain, Guid id, string planId) => new()
    {
        Id = id,
        Name = subdomain,
        Subdomain = subdomain,
        Status = TenantStatus.Active,
        PlanId = planId,
        CreatedAt = DateTime.UtcNow,
    };

    private static Role Role(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        IsBuiltIn = true,
        CreatedAt = DateTime.UtcNow,
    };

    private async Task SeedLocalAdminAsync(Guid tenantId)
    {
        await using var db = Context(tenantId);

        // Ensure the Tenant Admin role exists for this tenant, then attach a password-based admin membership.
        var adminRole = await db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == PermissionCatalog.BuiltInRoles.TenantAdmin);
        if (adminRole is null)
        {
            adminRole = Role(tenantId, PermissionCatalog.BuiltInRoles.TenantAdmin);
            db.Roles.Add(adminRole);
        }

        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Email = $"admin-{userId:N}@acme.com",
            DisplayName = "Local Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Br3akGl@ss!", workFactor: 4),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        var membershipId = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant
        {
            Id = membershipId,
            UserId = userId,
            TenantId = tenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UserTenantRoles = { new UserTenantRole { UserTenantId = membershipId, RoleId = adminRole.Id, AssignedAt = DateTime.UtcNow } },
        });

        await db.SaveChangesAsync();
    }

    private AppDbContext Context(Guid tenantId)
    {
        var tc = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(currentUser))
            .Options;
        return new AppDbContext(options, tc);
    }

    private AuthService Service(Guid tenantId) => new(
        Context(tenantId),
        _jwtService,
        new MutableTenantContext { TenantId = tenantId },
        Substitute.For<ITotpService>(),
        _configuration,
        _sharedCache,
        NullLogger<AuthService>.Instance,
        Substitute.For<IBackgroundJobClient>());

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }
}
