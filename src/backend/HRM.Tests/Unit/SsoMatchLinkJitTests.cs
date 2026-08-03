// ============================================================================
// US-AUTH-014 (SSO match / link / JIT provisioning) — ISSUE-337 + ISSUE-334.
//
// ISSUE-337: US-AUTH-014 shipped with seven IEEE-829 test cases (TC-AUTH-155..160,
// TC-AUTH-ISO-008) and ZERO automated arms behind them. ISSUE-332 closed the
// document gap, which made the traceability matrix LOOK complete while nothing
// executed — arguably worse than the original hole. These are the executing arms.
//
// ISSUE-334: JIT auto-provisioning emitted no `sso_jit_provisioned` audit event, so
// an account created straight out of an IdP assertion was indistinguishable from an
// ordinary sign-in in the audit trail. Covered here alongside TC-AUTH-157 because it
// is the same code path and the same seeding.
//
// Harness: the REAL AuthService over EF InMemory, mirroring SsoFailureAuditWriteTests.
// The behaviours under test (match precedence, link persistence, fail-closed refusal,
// audit inserts) are provider-independent — they are predicate and insert logic, not
// ledger arithmetic, so InMemory does not mask them here.
// ============================================================================

using FluentAssertions;
using Hangfire;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Auth.DTOs;
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

[Trait("US", "US-AUTH-014")]
public sealed class SsoMatchLinkJitTests
{
    private const string Subdomain = "acme";
    private const string Email = "user@acme.test";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public SsoMatchLinkJitTests()
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
    }

    // ── TC-AUTH-155: match by Entra oid — no duplicate, no role change ────────

    [Fact]
    [Trait("TC", "TC-AUTH-155")]
    public async Task ExistingUserLinkedByOid_IsMatched_WithoutDuplicateOrRoleChange()
    {
        var oid = Guid.NewGuid().ToString();
        await SeedAsync(oid: oid, roleName: PermissionCatalog.BuiltInRoles.Employee);

        var result = await CreateService().SsoSignInAsync(Identity(oid), default);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        db.Users.IgnoreQueryFilters().Count(u => u.Email == Email)
            .Should().Be(1, "matching by oid must not create a second account");
        db.UserTenants.IgnoreQueryFilters().Count(ut => ut.UserId == _userId && ut.TenantId == _tenantId)
            .Should().Be(1, "an existing membership must be reused, not duplicated");

        var roles = db.UserTenantRoles.IgnoreQueryFilters()
            .Include(utr => utr.Role)
            .Where(utr => utr.UserTenant!.UserId == _userId)
            .Select(utr => utr.Role!.Name)
            .ToList();
        roles.Should().ContainSingle().Which
            .Should().Be(PermissionCatalog.BuiltInRoles.Employee, "sign-in must never re-assign roles");
    }

    // ── TC-AUTH-156: bootstrap by verified email, then persist the oid link ───

    [Fact]
    [Trait("TC", "TC-AUTH-156")]
    public async Task FirstSignIn_MatchesByVerifiedEmail_AndPersistsTheOidLink()
    {
        // The user pre-exists with a membership but no Entra link yet (invited locally, first SSO login).
        await SeedAsync(oid: null, roleName: PermissionCatalog.BuiltInRoles.Employee);
        var oid = Guid.NewGuid().ToString();

        var result = await CreateService().SsoSignInAsync(Identity(oid), default);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        var user = db.Users.IgnoreQueryFilters().Single(u => u.Email == Email);
        user.EntraObjectId.Should().Be(oid,
            "the oid must be persisted on first match so FUTURE logins match by oid, not by mutable email");
        db.Users.IgnoreQueryFilters().Count(u => u.Email == Email)
            .Should().Be(1, "email-matching must link the existing account, never create a parallel one");
    }

    // ── TC-AUTH-157 + ISSUE-334: JIT provisioning + its audit event ───────────

    [Fact]
    [Trait("TC", "TC-AUTH-157")]
    public async Task JitAllowed_WithNoMembership_ProvisionsUserWithDefaultRole()
    {
        // Tenant + role exist, but no user and no membership — the pure JIT case.
        await SeedTenantAndRoleAsync(PermissionCatalog.BuiltInRoles.Employee);

        var oid = Guid.NewGuid().ToString();
        var result = await CreateService().SsoSignInAsync(
            Identity(oid, jitAllowed: true, defaultRole: PermissionCatalog.BuiltInRoles.Employee), default);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        var user = db.Users.IgnoreQueryFilters().Single(u => u.Email == Email);
        user.EntraObjectId.Should().Be(oid);
        user.PasswordHash.Should().BeNull("a JIT-provisioned SSO account has no local password");

        var membership = db.UserTenants.IgnoreQueryFilters()
            .Include(ut => ut.UserTenantRoles).ThenInclude(utr => utr.Role)
            .Single(ut => ut.UserId == user.Id && ut.TenantId == _tenantId);
        membership.Status.Should().Be(UserTenantStatus.Active);
        membership.UserTenantRoles.Should().ContainSingle().Which.Role!.Name
            .Should().Be(PermissionCatalog.BuiltInRoles.Employee, "the jit_default_role governs the new membership");
    }

    /// <summary>
    /// ISSUE-334: auto-provisioning must be distinguishable from an ordinary login in the audit trail.
    /// Asserts the AC-named event AND that it records the account was created, not merely joined.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-AUTH-157")]
    public async Task JitProvisioning_WritesSsoJitProvisionedAuditEvent()
    {
        await SeedTenantAndRoleAsync(PermissionCatalog.BuiltInRoles.Employee);

        var oid = Guid.NewGuid().ToString();
        (await CreateService().SsoSignInAsync(
            Identity(oid, jitAllowed: true, defaultRole: PermissionCatalog.BuiltInRoles.Employee), default))
            .IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var rows = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == "sso_jit_provisioned").ToList();

        rows.Should().ContainSingle("auto-provisioning must leave exactly one AC-named audit row");
        rows[0].TenantId.Should().Be(_tenantId, "the event belongs to the tenant the user was provisioned into");
        rows[0].UserId.Should().NotBeNull("the provisioned user must be named");
        rows[0].Detail.Should().NotBeNull();
        rows[0].Detail!.Should().Contain("\"CreatedUser\":true",
            "an account created out of an IdP assertion is a different event from an existing user joining");
        rows[0].Detail!.Should().Contain(PermissionCatalog.BuiltInRoles.Employee,
            "the granted role is the security-relevant fact an auditor needs");
    }

    /// <summary>
    /// The other half of ISSUE-334's distinction: an EXISTING user gaining a membership is still a JIT
    /// provisioning event, but must not claim the account was created. Without this arm a hard-coded
    /// `CreatedUser = true` would pass the arm above.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-AUTH-157")]
    public async Task JitMembershipForExistingUser_AuditsWithoutClaimingAccountCreation()
    {
        var oid = Guid.NewGuid().ToString();
        await SeedTenantAndRoleAsync(PermissionCatalog.BuiltInRoles.Employee);
        using (var seed = CreateDbContext())
        {
            seed.Users.Add(new User
            {
                Id = _userId,
                Email = Email,
                DisplayName = "Acme User",
                PasswordHash = null,
                IsActive = true,
                IdentityProvider = "entra",
                EntraObjectId = oid,
                CreatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        (await CreateService().SsoSignInAsync(
            Identity(oid, jitAllowed: true, defaultRole: PermissionCatalog.BuiltInRoles.Employee), default))
            .IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var row = db.AuditLogs.IgnoreQueryFilters().Single(a => a.EventType == "sso_jit_provisioned");
        row.Detail!.Should().Contain("\"CreatedUser\":false",
            "the user already existed — only the membership was provisioned");
    }

    // ── TC-AUTH-158: JIT disabled → fail closed, create nothing ───────────────

    [Fact]
    [Trait("TC", "TC-AUTH-158")]
    public async Task JitDisabled_WithNoMembership_IsRefused_AndCreatesNothing()
    {
        await SeedTenantAndRoleAsync(PermissionCatalog.BuiltInRoles.Employee);

        var result = await CreateService().SsoSignInAsync(
            Identity(Guid.NewGuid().ToString(), jitAllowed: false), default);

        result.IsFailure.Should().BeTrue("JIT is off, so an unknown identity must be refused");
        result.StatusCode.Should().Be(403);

        using var db = CreateDbContext();
        db.Users.IgnoreQueryFilters().Any(u => u.Email == Email)
            .Should().BeFalse("a refused sign-in must not leave a half-provisioned account");
        db.UserTenants.IgnoreQueryFilters().Any(ut => ut.TenantId == _tenantId)
            .Should().BeFalse("nor a membership");
        db.AuditLogs.IgnoreQueryFilters().Any(a => a.EventType == "sso_jit_provisioned")
            .Should().BeFalse("nothing was provisioned, so nothing may claim it was");
    }

    // ── TC-AUTH-159: JIT privilege ceiling on the settings write ──────────────

    [Theory]
    [Trait("TC", "TC-AUTH-159")]
    [InlineData("Tenant Owner")]
    [InlineData("Tenant Admin")]
    public async Task JitDefaultRole_CannotBeAPrivilegedRole(string privilegedRole)
    {
        await SeedTenantAndRoleAsync(privilegedRole);

        var result = await CreateService().UpdateTenantAuthSettingsAsync(
            _tenantId,
            new TenantAuthSettingsRequest { JitEnabled = true, JitDefaultRole = privilegedRole },
            default);

        result.IsFailure.Should().BeTrue(
            $"'{privilegedRole}' is above the JIT ceiling — an IdP assertion must not mint an administrator");

        using var db = CreateDbContext();
        db.Tenants.IgnoreQueryFilters().Single(t => t.Id == _tenantId).JitDefaultRole
            .Should().BeNull("a rejected settings write must change nothing");
    }

    [Fact]
    [Trait("TC", "TC-AUTH-159")]
    public async Task JitDefaultRole_AcceptsANonPrivilegedRole()
    {
        // The positive control: the ceiling must reject privileged roles WITHOUT rejecting everything.
        await SeedTenantAndRoleAsync(PermissionCatalog.BuiltInRoles.Employee);

        var result = await CreateService().UpdateTenantAuthSettingsAsync(
            _tenantId,
            new TenantAuthSettingsRequest
            {
                JitEnabled = true,
                JitDefaultRole = PermissionCatalog.BuiltInRoles.Employee,
            },
            default);

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    // ── TC-AUTH-160: inactive user / inactive membership are refused ──────────

    [Fact]
    [Trait("TC", "TC-AUTH-160")]
    public async Task InactiveUser_IsRefused()
    {
        var oid = Guid.NewGuid().ToString();
        await SeedAsync(oid, PermissionCatalog.BuiltInRoles.Employee, userActive: false);

        var result = await CreateService().SsoSignInAsync(Identity(oid), default);

        result.IsFailure.Should().BeTrue("a deactivated account must not be revived by an SSO assertion");
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    [Trait("TC", "TC-AUTH-160")]
    public async Task InactiveMembership_IsRefused()
    {
        var oid = Guid.NewGuid().ToString();
        await SeedAsync(oid, PermissionCatalog.BuiltInRoles.Employee,
            membershipStatus: UserTenantStatus.Suspended);

        var result = await CreateService().SsoSignInAsync(Identity(oid), default);

        result.IsFailure.Should().BeTrue("a suspended workspace membership must not sign in");
        result.StatusCode.Should().Be(403);
    }

    // ── TC-AUTH-ISO-008: match/JIT is strictly tenant-scoped ─────────────────

    [Fact]
    [Trait("TC", "TC-AUTH-ISO-008")]
    public async Task JitProvisioningIntoOneTenant_GrantsNoAccessToAnother()
    {
        await SeedTenantAndRoleAsync(PermissionCatalog.BuiltInRoles.Employee);
        using (var seed = CreateDbContext())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = _otherTenantId,
                Name = "Globex",
                Subdomain = "globex",
                Status = TenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var oid = Guid.NewGuid().ToString();
        (await CreateService().SsoSignInAsync(
            Identity(oid, jitAllowed: true, defaultRole: PermissionCatalog.BuiltInRoles.Employee), default))
            .IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var user = db.Users.IgnoreQueryFilters().Single(u => u.Email == Email);

        db.UserTenants.IgnoreQueryFilters().Count(ut => ut.UserId == user.Id)
            .Should().Be(1, "JIT must grant access to exactly the signing-in tenant");
        db.UserTenants.IgnoreQueryFilters().Any(ut => ut.UserId == user.Id && ut.TenantId == _otherTenantId)
            .Should().BeFalse("provisioning into acme must never leak a membership into globex");
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "sso_jit_provisioned" && a.TenantId == _otherTenantId)
            .Should().BeFalse("nor attribute the provisioning event to the wrong tenant");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private SsoIdentity Identity(string oid, bool jitAllowed = false, string? defaultRole = null) => new()
    {
        Subdomain = Subdomain,
        TenantId = Guid.NewGuid().ToString(), // Entra directory id (tid) — audit context only
        ObjectId = oid,
        Email = Email,
        DisplayName = "Acme User",
        JitAllowed = jitAllowed,
        DefaultRole = defaultRole,
        IpAddress = "203.0.113.22",
        UserAgent = "xUnit",
    };

    private AuthService CreateService() => new(
        CreateDbContext(),
        _jwtService,
        _tenantContext,
        Substitute.For<ITotpService>(),
        _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(),
        Substitute.For<IBackgroundJobClient>());

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private const string PlanCode = "pro";

    /// <summary>
    /// Seeds an SSO-ENTITLED plan alongside the tenant. This matters: `UpdateTenantAuthSettingsAsync` gates on
    /// `PlanFeatureFlags.Sso` (US-AUTH-012 FR-3) BEFORE it reaches the JIT privilege ceiling. Without the
    /// entitlement the TC-AUTH-159 arms would pass on `sso_not_entitled` and never exercise the ceiling at all —
    /// a false pass the positive-control arm exists to catch.
    /// </summary>
    private async Task SeedTenantAndRoleAsync(string roleName)
    {
        using var db = CreateDbContext();
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = BaseEntity.NewUuidV7(),
            Code = PlanCode,
            Name = "Pro",
            FeatureFlags = new PlanFeatureFlags { Sso = true },
            CreatedAt = DateTime.UtcNow,
        });
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Acme",
            Subdomain = Subdomain,
            Status = TenantStatus.Active,
            PlanId = PlanCode,
            CreatedAt = DateTime.UtcNow,
        });
        db.Roles.Add(new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = roleName,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(
        string? oid,
        string roleName,
        bool userActive = true,
        UserTenantStatus membershipStatus = UserTenantStatus.Active)
    {
        await SeedTenantAndRoleAsync(roleName);

        using var db = CreateDbContext();
        var role = db.Roles.IgnoreQueryFilters().First(r => r.TenantId == _tenantId && r.Name == roleName);

        db.Users.Add(new User
        {
            Id = _userId,
            Email = Email,
            DisplayName = "Acme User",
            PasswordHash = null,
            IsActive = userActive,
            IdentityProvider = oid is null ? null : "entra",
            EntraObjectId = oid,
            CreatedAt = DateTime.UtcNow,
        });

        var membership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantId,
            Status = membershipStatus,
            CreatedAt = DateTime.UtcNow,
        };
        membership.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "unit-test",
        });
        db.UserTenants.Add(membership);

        await db.SaveChangesAsync();
    }
}
