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

    // ── ISSUE-327 / TC-AUTH-127: break-glass via the TWO-STEP MFA path also audits + alerts ──

    [Fact]
    [Trait("TC", "TC-AUTH-127")]
    public async Task BreakGlassLogin_TwoStepMfa_ByDesignatedAdmin_UnderSsoOnly_AuditsAndAlerts()
    {
        // A designated break-glass admin who has MFA enrolled logs in under sso_only via the two-step MFA flow:
        //   step 1 = break-glass-login (returns an MFA challenge, no tokens yet),
        //   step 2 = mfa/verify (completes the login).
        // ISSUE-327: the break_glass_login audit event + admin alert must fire on the step-2 completion, exactly
        // as the single-shot path does — not just on the inline-mfaCode path.
        await SeedAsync(enforcementMode: SsoEnforcementModes.SsoOnly, designateUser: true, mfaEnabled: true);

        // Step 1: break-glass credentials with no MFA code → an MFA challenge, no break-glass emit yet.
        var challenge = await CreateService().BreakGlassLoginAsync(Email, Password, null, "203.0.113.5", "xUnit", default);
        challenge.IsSuccess.Should().BeTrue();
        challenge.Value!.MfaChallenge.Should().BeTrue();
        challenge.Value!.AccessToken.Should().BeNullOrEmpty("an MFA challenge issues no tokens");

        using (var db0 = CreateDbContext())
        {
            db0.AuditLogs.IgnoreQueryFilters()
                .Any(a => a.EventType == "break_glass_login" && a.UserId == _userId)
                .Should().BeFalse("break-glass is audited only on completed token issuance, not on the challenge");
        }

        // Step 2: verify the TOTP code → login completes → break_glass_login audit + admin alert must fire.
        var totp = Substitute.For<ITotpService>();
        totp.ValidateCode(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var verify = await CreateServiceWithTotp(totp).VerifyMfaLoginAsync(Email, "123456", "203.0.113.5", "xUnit", default);

        verify.IsSuccess.Should().BeTrue();
        verify.Value!.AccessToken.Should().NotBeNullOrEmpty();

        using var db = CreateDbContext();
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "break_glass_login" && a.UserId == _userId)
            .Should().BeTrue("a break-glass admin completing the two-step MFA verify is still a high-severity audited event (FR-4)");

        // NFR-2: the admin alert was enqueued on Hangfire from the MFA-verify path, same as the single-shot path.
        var createCall = _backgroundJobClient.ReceivedCalls()
            .FirstOrDefault(c => c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create));
        createCall.Should().NotBeNull("a break-glass alert must be enqueued from the MFA-verify path");
        var job = (Hangfire.Common.Job)createCall!.GetArguments()[0]!;
        job.Method.Name.Should().Be(nameof(IBreakGlassNotificationService.SendBreakGlassAlertAsync));
    }

    [Fact]
    [Trait("TC", "TC-AUTH-127")]
    public async Task TwoStepMfa_OrdinaryUser_UnderOptional_DoesNotEmitBreakGlass()
    {
        // Designation gate: an ordinary MFA user (NOT designated) completing the two-step MFA verify on a normal
        // (optional-enforcement) login must NOT trigger any break-glass telemetry. This proves the new MFA-path
        // emit is gated on the break-glass condition, not on "any completed MFA verify".
        await SeedAsync(enforcementMode: SsoEnforcementModes.Optional, designateUser: false, mfaEnabled: true);

        var totp = Substitute.For<ITotpService>();
        totp.ValidateCode(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var verify = await CreateServiceWithTotp(totp).VerifyMfaLoginAsync(Email, "123456", "127.0.0.1", "xUnit", default);

        verify.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "break_glass_login")
            .Should().BeFalse("an ordinary MFA login is not a break-glass event");

        _backgroundJobClient.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create))
            .Should().BeFalse("no break-glass alert should be enqueued for an ordinary MFA login");
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

    private AuthService CreateService() => CreateServiceWithTotp(Substitute.For<ITotpService>());

    private AuthService CreateServiceWithTotp(ITotpService totpService) => new(
        CreateDbContext(),
        _jwtService,
        _tenantContext,
        totpService,
        _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(),
        _backgroundJobClient);

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private async Task SeedAsync(string enforcementMode, bool designateUser, bool mfaEnabled = false)
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
            MfaEnabled = mfaEnabled,
            // Plaintext secret is fine here: AuthService's default protector is a passthrough and the TOTP
            // service is substituted in these arms (the secret's value is never cryptographically checked).
            MfaSecret = mfaEnabled ? "JBSWY3DPEHPK3PXP" : null,
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
