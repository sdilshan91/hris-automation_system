// ============================================================================
// US-AUTH-016 — SSO enforcement at login + break-glass + admin-consent capture,
// exercised against the real AuthService over the EF InMemory provider (no raw
// SQL on these paths). Covers the enforcement DECISION (FR-1/AC-1), the
// break-glass allow/deny (FR-2/AC-2/AC-7/BR-2), the break-glass audit + alert
// (FR-4/NFR-2), and the admin-consent capture/failure (AC-5/AC-6/BR-3).
//
// The jsonb persistence, the sso_only WRITE guard, real login enforcement over
// Postgres, and cross-tenant isolation are covered by the Testcontainers arms in
// TenantSsoSettingsPostgresTests / SsoLoginEnforcementPostgresTests.
// ============================================================================

using FluentAssertions;
using Hangfire;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
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

[Trait("TC", "TC-AUTH-016")]
public sealed class SsoEnforcementLoginTests
{
    private const string Email = "admin@test.local";
    private const string Password = "Br3akGl@ss!";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public SsoEnforcementLoginTests()
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

    // ── FR-1/AC-1: standard local login is refused under sso_only ─────────────

    [Fact]
    public async Task StandardLogin_UnderSsoOnly_IsRefusedWithMicrosoftMessage()
    {
        await SeedAsync(enforcementMode: SsoEnforcementModes.SsoOnly, designateUser: false);

        var result = await CreateService().LoginAsync(Email, Password, null, "127.0.0.1", "xUnit", default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("requires sign-in with Microsoft");
    }

    [Fact]
    public async Task StandardLogin_UnderSsoOnly_IsRefused_EvenForADesignatedAdmin()
    {
        // AC-1: the STANDARD path is refused for everyone under sso_only — a designated admin must use break-glass.
        await SeedAsync(enforcementMode: SsoEnforcementModes.SsoOnly, designateUser: true);

        var result = await CreateService().LoginAsync(Email, Password, null, "127.0.0.1", "xUnit", default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task StandardLogin_UnderOptional_Succeeds()
    {
        await SeedAsync(enforcementMode: SsoEnforcementModes.Optional, designateUser: false);

        var result = await CreateService().LoginAsync(Email, Password, null, "127.0.0.1", "xUnit", default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrEmpty();
    }

    // ── FR-2/AC-2: designated break-glass admin can sign in under sso_only ────

    [Fact]
    public async Task BreakGlassLogin_ByDesignatedAdmin_UnderSsoOnly_Succeeds_AndAuditsAndAlerts()
    {
        await SeedAsync(enforcementMode: SsoEnforcementModes.SsoOnly, designateUser: true);

        var result = await CreateService().BreakGlassLoginAsync(Email, Password, null, "203.0.113.5", "xUnit", default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrEmpty();

        using var db = CreateDbContext();
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "break_glass_login" && a.UserId == _userId)
            .Should().BeTrue("a successful break-glass login is a high-severity audited event (FR-4)");

        // NFR-2: an admin alert was enqueued on Hangfire (dispatched off the login path).
        var createCall = _backgroundJobClient.ReceivedCalls()
            .FirstOrDefault(c => c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create));
        createCall.Should().NotBeNull("a break-glass alert must be enqueued");
        var job = (Hangfire.Common.Job)createCall!.GetArguments()[0]!;
        job.Method.Name.Should().Be(nameof(IBreakGlassNotificationService.SendBreakGlassAlertAsync));
    }

    // ── AC-7/BR-2: break-glass is restricted to designated admins ─────────────

    [Fact]
    public async Task BreakGlassLogin_ByNonDesignatedUser_IsRefused()
    {
        await SeedAsync(enforcementMode: SsoEnforcementModes.SsoOnly, designateUser: false);

        var result = await CreateService().BreakGlassLoginAsync(Email, Password, null, "127.0.0.1", "xUnit", default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("restricted to designated administrators");

        using var db = CreateDbContext();
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "break_glass_login_denied" && a.UserId == _userId)
            .Should().BeTrue();
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "break_glass_login")
            .Should().BeFalse("a denied attempt never emits the success event");
    }

    [Fact]
    public async Task BreakGlassLogin_WrongPassword_IsRefused()
    {
        await SeedAsync(enforcementMode: SsoEnforcementModes.SsoOnly, designateUser: true);

        var result = await CreateService().BreakGlassLoginAsync(Email, "wrong-password", null, "127.0.0.1", "xUnit", default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
    }

    // ── AC-5/AC-6/BR-3: admin-consent capture + failure ───────────────────────

    [Fact]
    public async Task CaptureAdminConsent_AddsTid_Consented_WithoutEnabling()
    {
        await SeedAsync(enforcementMode: SsoEnforcementModes.Optional, designateUser: false);
        var tid = Guid.NewGuid().ToString();

        var result = await CreateService().CaptureAdminConsentAsync("testco", tid, "203.0.113.1", "xUnit", default);
        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var t = await db.Tenants.IgnoreQueryFilters().FirstAsync(x => x.Id == _tenantId);
        t.AllowedEntraTenantIds.Should().Contain(tid);
        t.SsoOnboardingStatus.Should().Be(SsoOnboardingStatuses.Consented);
        t.SsoEnabled.Should().BeFalse("BR-3: consent alone never enables SSO");
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "sso_admin_consent_completed").Should().BeTrue();
    }

    [Fact]
    public async Task RecordAdminConsentFailure_DoesNotEnable_AndAudits()
    {
        await SeedAsync(enforcementMode: SsoEnforcementModes.Optional, designateUser: false);

        var result = await CreateService().RecordAdminConsentFailureAsync("testco", "access_denied", "203.0.113.2", "xUnit", default);
        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var t = await db.Tenants.IgnoreQueryFilters().FirstAsync(x => x.Id == _tenantId);
        t.SsoEnabled.Should().BeFalse();
        t.SsoEnforcementMode.Should().Be(SsoEnforcementModes.Optional);
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "sso_admin_consent_failed").Should().BeTrue();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

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

    private async Task SeedAsync(string enforcementMode, bool designateUser)
    {
        using var db = CreateDbContext();

        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = PermissionCatalog.BuiltInRoles.TenantAdmin,
            CreatedAt = DateTime.UtcNow,
        };

        var tenant = new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Subdomain = "testco",
            Status = TenantStatus.Active,
            SsoEnforcementMode = enforcementMode,
            BreakGlassAdminUserIds = designateUser ? [_userId.ToString()] : [],
            CreatedAt = DateTime.UtcNow,
        };

        var user = new User
        {
            Id = _userId,
            Email = Email,
            DisplayName = "Break-Glass Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var membership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        membership.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "unit-test",
        });

        db.Roles.Add(role);
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.UserTenants.Add(membership);

        await db.SaveChangesAsync();
    }
}
