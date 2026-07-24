// ============================================================================
// US-AUTH-016 — SSO enforcement on the REAL login path, over REAL PostgreSQL via
// Testcontainers (NOT InMemory): the jsonb break-glass list round-trips, the
// cached-snapshot enforcement decision is honoured against a real DB, and one
// tenant's sso_only enforcement never affects another tenant's login (BR-6/NFR).
//
// Covers: AC-1 (standard local login refused under sso_only), AC-2 (designated
// break-glass admin permitted under sso_only + break_glass_login audit), AC-7
// (non-designated user refused on the break-glass path), and cross-tenant
// isolation (an optional tenant still logs in while a sibling is sso_only).
// ============================================================================

using FluentAssertions;
using Hangfire;
using HRM.Application.Common.Interfaces;
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

[Trait("TC", "TC-AUTH-016")]
public sealed class SsoLoginEnforcementPostgresTests : IAsyncLifetime
{
    private const string Password = "Br3akGl@ss!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();   // sso_only, with a designated break-glass admin
    private readonly Guid _tenantB = Guid.NewGuid();   // optional — isolation counterpart

    private readonly Guid _adminA = Guid.NewGuid();     // designated break-glass admin of A
    private readonly Guid _plainA = Guid.NewGuid();     // ordinary (non-designated) admin of A
    private readonly Guid _userB = Guid.NewGuid();      // ordinary user of B

    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "hrm-api-test",
            ["Jwt:Audience"] = "hrm-client-test",
            ["Platform:BaseDomain"] = "yourhrm.test",
        })
        .Build();

    private readonly IDistributedCache _sharedCache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private JwtService _jwtService = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _jwtService = new JwtService(_configuration);

        await using var db = Context(_tenantA);
        await db.Database.MigrateAsync();

        db.Tenants.Add(Tenant("acme", _tenantA, SsoEnforcementModes.SsoOnly, [_adminA.ToString()]));
        db.Tenants.Add(Tenant("globex", _tenantB, SsoEnforcementModes.Optional, []));

        db.Roles.Add(Role(_tenantA, PermissionCatalog.BuiltInRoles.TenantAdmin));
        db.Roles.Add(Role(_tenantB, PermissionCatalog.BuiltInRoles.TenantAdmin));

        AddUser(db, _adminA, _tenantA, "admin-a@acme.com");
        AddUser(db, _plainA, _tenantA, "plain-a@acme.com");
        AddUser(db, _userB, _tenantB, "user-b@globex.com");

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── AC-1: standard local login refused under sso_only ─────────────────────

    [Fact]
    public async Task StandardLogin_UnderSsoOnly_IsRefused()
    {
        var result = await Service(_tenantA).LoginAsync("admin-a@acme.com", Password, null, "127.0.0.1", "xUnit", default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("requires sign-in with Microsoft");
    }

    // ── AC-2: designated break-glass admin permitted under sso_only ───────────

    [Fact]
    public async Task BreakGlassLogin_ByDesignatedAdmin_Succeeds_AndAudits()
    {
        var background = Substitute.For<IBackgroundJobClient>();
        var result = await Service(_tenantA, background)
            .BreakGlassLoginAsync("admin-a@acme.com", Password, null, "203.0.113.5", "xUnit", default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrEmpty();

        await using var verify = Context(_tenantA);
        (await verify.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(a => a.TenantId == _tenantA && a.EventType == "break_glass_login" && a.UserId == _adminA))
            .Should().BeTrue();

        background.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create))
            .Should().BeTrue("a break-glass admin alert must be enqueued (NFR-2)");
    }

    // ── AC-7: a non-designated user cannot use the break-glass path ───────────

    [Fact]
    public async Task BreakGlassLogin_ByNonDesignatedUser_IsRefused()
    {
        var result = await Service(_tenantA).BreakGlassLoginAsync("plain-a@acme.com", Password, null, "127.0.0.1", "xUnit", default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("restricted to designated administrators");
    }

    // ── BR-6/NFR: one tenant's enforcement never affects another ──────────────

    [Fact]
    public async Task Isolation_OptionalTenant_StillLogsIn_WhileSiblingIsSsoOnly()
    {
        var result = await Service(_tenantB).LoginAsync("user-b@globex.com", Password, null, "127.0.0.1", "xUnit", default);

        result.IsSuccess.Should().BeTrue("tenant B is 'optional' — tenant A's sso_only enforcement must not leak across tenants");
        result.Value!.AccessToken.Should().NotBeNullOrEmpty();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Tenant Tenant(string subdomain, Guid id, string enforcementMode, List<string> breakGlass) => new()
    {
        Id = id,
        Name = subdomain,
        Subdomain = subdomain,
        Status = TenantStatus.Active,
        PlanId = "default",
        SsoEnforcementMode = enforcementMode,
        BreakGlassAdminUserIds = breakGlass,
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

    private static void AddUser(AppDbContext db, Guid userId, Guid tenantId, string email)
    {
        db.Users.Add(new User
        {
            Id = userId,
            Email = email,
            DisplayName = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        var adminRole = db.Roles.Local.First(r => r.TenantId == tenantId);
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

    private AuthService Service(Guid tenantId, IBackgroundJobClient? background = null) => new(
        Context(tenantId),
        _jwtService,
        new MutableTenantContext { TenantId = tenantId },
        Substitute.For<ITotpService>(),
        _configuration,
        _sharedCache,
        NullLogger<AuthService>.Instance,
        background ?? Substitute.For<IBackgroundJobClient>());

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
