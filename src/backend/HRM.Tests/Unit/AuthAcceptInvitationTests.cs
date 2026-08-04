// ============================================================================
// BUG-294 regression: a tenant user-invitation must be REDEEMABLE.
//
// Pre-fix, UserInvitation.TokenHash was minted, stored and rotated on resend but
// NEVER verified anywhere, and no endpoint accepted it — InvitationStatus.Accepted
// was unreachable code. Every invitation email carried a live one-time token to a
// route the backend could not honour, so an invited user could never sign in, and
// the admin console reported the invite as "sent".
//
// These arms mirror AuthPasswordResetTests (the sibling token rail): no-token,
// wrong-token, expired, replay, plus the invitation-specific states (revoked,
// already-accepted, rotated-by-resend) and the cross-tenant guard.
//
// Provider: EF InMemory through the real AuthService — the defect is token
// validation + membership wiring, which is provider-independent.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
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

public sealed class AuthAcceptInvitationTests
{
    private const string InviteeEmail = "newjoiner@acme.com";
    private const string ChosenPassword = "BrandNewPassw0rd!";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Guid _invitationId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public AuthAcceptInvitationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
        _tenantContext.Subdomain.Returns("acme");

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

    // ── Seeding ─────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the state the invite path leaves behind: a PASSWORDLESS global user, a role to be granted, and an
    /// invitation row — and deliberately NO UserTenant, because membership is what acceptance creates.
    /// </summary>
    private async Task SeedAsync(
        string rawToken,
        InvitationStatus status = InvitationStatus.Invited,
        DateTime? expiresAt = null,
        Guid? tenantOverride = null,
        bool includeRole = true,
        bool includeUser = true)
    {
        using var db = CreateDbContext();
        var tenantId = tenantOverride ?? _tenantId;

        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "acme", Name = "Acme" });

        if (includeUser)
        {
            db.Users.Add(new User
            {
                Id = _userId,
                Email = InviteeEmail,
                PasswordHash = null, // invited users are created passwordless
                IsActive = true,
            });
        }

        if (includeRole)
        {
            db.Roles.Add(new Role
            {
                Id = _roleId,
                TenantId = tenantId,
                Name = "Employee",
            });
        }

        db.UserInvitations.Add(new UserInvitation
        {
            Id = _invitationId,
            TenantId = tenantId,
            Email = InviteeEmail,
            TokenHash = InvitationToken.Hash(rawToken),
            Status = status,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(72),
            InvitedByUserId = Guid.NewGuid(),
            InvitedRoleIds = includeRole ? new List<Guid> { _roleId } : new List<Guid>(),
        });

        await db.SaveChangesAsync();
    }

    // ── Happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_WithValidToken_ActivatesMembership_GrantsRoles_AndSetsPassword()
    {
        await SeedAsync("the-real-token");

        var result = await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();

        var membership = await db.UserTenants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(ut => ut.UserId == _userId && ut.TenantId == _tenantId);
        membership.Should().NotBeNull("acceptance is what creates the membership");
        membership!.Status.Should().Be(UserTenantStatus.Active);

        var hasRole = await db.UserTenantRoles.IgnoreQueryFilters()
            .AnyAsync(x => x.UserTenantId == membership.Id && x.RoleId == _roleId);
        hasRole.Should().BeTrue("the roles chosen at invite time are granted on acceptance");

        var invitation = await db.UserInvitations.IgnoreQueryFilters().SingleAsync(i => i.Id == _invitationId);
        invitation.Status.Should().Be(InvitationStatus.Accepted, "the status was previously unreachable");
        invitation.AcceptedAt.Should().NotBeNull();

        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == _userId);
        user.PasswordHash.Should().NotBeNullOrEmpty("the invitee set their first password");
        BCrypt.Net.BCrypt.Verify(ChosenPassword, user.PasswordHash).Should().BeTrue(
            "the stored hash must actually verify the chosen password — a non-null hash alone proves nothing");
    }

    [Fact]
    public async Task Accept_ThenLogin_IsPossible_TheWholePointOfTheFix()
    {
        // The end-to-end statement of the bug: before the fix an invited user could never authenticate,
        // because login rejects a null PasswordHash outright.
        await SeedAsync("the-real-token");

        (await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword))
            .IsSuccess.Should().BeTrue();

        var login = await CreateService().LoginAsync(
            InviteeEmail, ChosenPassword, mfaCode: null, ipAddress: null, userAgent: null);

        login.IsSuccess.Should().BeTrue(
            "an accepted invitation must produce an account that can actually sign in — {0}", login.Error);
    }

    // ── Rejection arms ──────────────────────────────────────────────────

    [Fact]
    public async Task Accept_WithWrongToken_IsRejected_AndNothingIsProvisioned()
    {
        await SeedAsync("the-real-token");

        var result = await CreateService().AcceptInvitationAsync("a-different-token", ChosenPassword);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        await AssertNothingProvisionedAsync();
    }

    [Fact]
    public async Task Accept_WithEmptyToken_IsRejected()
    {
        await SeedAsync("the-real-token");

        var result = await CreateService().AcceptInvitationAsync("", ChosenPassword);

        result.IsFailure.Should().BeTrue();
        await AssertNothingProvisionedAsync();
    }

    [Fact]
    public async Task Accept_WithExpiredInvitation_IsRejected()
    {
        await SeedAsync("the-real-token", expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var result = await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword);

        result.IsFailure.Should().BeTrue();
        await AssertNothingProvisionedAsync();
    }

    [Fact]
    public async Task Accept_WithRevokedInvitation_IsRejected()
    {
        // Revocation must actually revoke — an admin who withdraws an invitation has to be able to rely on it.
        await SeedAsync("the-real-token", status: InvitationStatus.Revoked);

        var result = await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword);

        result.IsFailure.Should().BeTrue();
        await AssertNothingProvisionedAsync();
    }

    [Fact]
    public async Task Accept_AlreadyAcceptedInvitation_IsRejected()
    {
        await SeedAsync("the-real-token", status: InvitationStatus.Accepted);

        var result = await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Accept_IsSingleUse_ReplayingTheSameLinkIsRejected()
    {
        await SeedAsync("the-real-token");

        (await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword))
            .IsSuccess.Should().BeTrue();

        // A second use must not, for example, re-grant roles or reset the password again.
        var replay = await CreateService().AcceptInvitationAsync("the-real-token", "YetAnotherPassw0rd!");

        replay.IsFailure.Should().BeTrue("an accepted invitation is spent");

        using var db = CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == _userId);
        BCrypt.Net.BCrypt.Verify(ChosenPassword, user.PasswordHash).Should().BeTrue(
            "the replay must not have overwritten the password set by the legitimate acceptance");
    }

    [Fact]
    public async Task Accept_WithTokenRotatedByAResend_RejectsTheOldToken()
    {
        // Resend rotates TokenHash (pinned by TenantUserManagementIntegrationTests). The superseded link must
        // stop working, otherwise "resend" would silently widen the window instead of replacing it.
        await SeedAsync("the-original-token");

        using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.IgnoreQueryFilters().SingleAsync(i => i.Id == _invitationId);
            invitation.TokenHash = InvitationToken.Hash("the-rotated-token");
            await db.SaveChangesAsync();
        }

        var stale = await CreateService().AcceptInvitationAsync("the-original-token", ChosenPassword);
        stale.IsFailure.Should().BeTrue("the superseded token must not survive a resend");

        var fresh = await CreateService().AcceptInvitationAsync("the-rotated-token", ChosenPassword);
        fresh.IsSuccess.Should().BeTrue(fresh.Error);
    }

    [Fact]
    public async Task Accept_TokenFromAnotherTenant_IsNotRedeemableUnderThisTenant()
    {
        // Tenant isolation: the invitation belongs to a different tenant, so under this tenant's context the
        // global query filter must make it invisible rather than merely "not matching".
        await SeedAsync("the-real-token", tenantOverride: Guid.NewGuid());

        var result = await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword);

        result.IsFailure.Should().BeTrue("a tenant-A invitation must not be redeemable on tenant B's subdomain");
        await AssertNothingProvisionedAsync();
    }

    [Fact]
    public async Task Accept_WithUnresolvedTenant_IsRejected()
    {
        await SeedAsync("the-real-token");

        var unresolved = Substitute.For<ITenantContext>();
        unresolved.IsResolved.Returns(false);
        unresolved.TenantId.Returns(Guid.Empty);

        var service = new AuthService(
            TestDbContextFactory.Create(unresolved, _dbName),
            _jwtService,
            unresolved,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>());

        var result = await service.AcceptInvitationAsync("the-real-token", ChosenPassword);

        result.IsFailure.Should().BeTrue("without a resolved workspace there is no invitation scope to search");
    }

    // ── Robustness ──────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_WhenAnInvitedRoleWasDeleted_StillSucceeds_AndSkipsTheMissingRole()
    {
        // A role can be deleted during the 72-hour window. Granting a dangling id would blow up on the FK and
        // make a legitimate invitation permanently unredeemable — the user must still get in.
        await SeedAsync("the-real-token", includeRole: false);

        using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.IgnoreQueryFilters().SingleAsync(i => i.Id == _invitationId);
            invitation.InvitedRoleIds = new List<Guid> { Guid.NewGuid() }; // never existed
            await db.SaveChangesAsync();
        }

        var result = await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var assertDb = CreateDbContext();
        var membership = await assertDb.UserTenants.IgnoreQueryFilters()
            .SingleAsync(ut => ut.UserId == _userId && ut.TenantId == _tenantId);
        (await assertDb.UserTenantRoles.IgnoreQueryFilters()
            .CountAsync(x => x.UserTenantId == membership.Id))
            .Should().Be(0, "the vanished role is skipped rather than blocking the whole redemption");
    }

    [Fact]
    public async Task Accept_WithAPasswordThatFailsPolicy_LeavesTheInvitationUsable()
    {
        // The password rail rejects before saving, so a weak first attempt must not burn the invitation —
        // otherwise a typo would permanently lock the invitee out.
        await SeedAsync("the-real-token");

        var weak = await CreateService().AcceptInvitationAsync("the-real-token", "short");
        weak.IsFailure.Should().BeTrue();

        using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.IgnoreQueryFilters().SingleAsync(i => i.Id == _invitationId);
            invitation.Status.Should().Be(InvitationStatus.Invited, "a rejected password must not consume the invitation");
        }

        var retry = await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword);
        retry.IsSuccess.Should().BeTrue(retry.Error);
    }

    [Fact]
    public async Task Accept_ReactivatesADisabledMembership_RatherThanCreatingASecond()
    {
        await SeedAsync("the-real-token");

        using (var db = CreateDbContext())
        {
            db.UserTenants.Add(new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _tenantId,
                Status = UserTenantStatus.Disabled,
            });
            await db.SaveChangesAsync();
        }

        var result = await CreateService().AcceptInvitationAsync("the-real-token", ChosenPassword);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var assertDb = CreateDbContext();
        var memberships = await assertDb.UserTenants.IgnoreQueryFilters()
            .Where(ut => ut.UserId == _userId && ut.TenantId == _tenantId)
            .ToListAsync();

        memberships.Should().ContainSingle("re-accepting must not duplicate the membership row");
        memberships[0].Status.Should().Be(UserTenantStatus.Active);
    }

    [Fact]
    public async Task Accept_InvalidatesTheCachedPermissionsAndTenantList()
    {
        // The membership is Active only as of this commit, so a user invited into a SECOND workspace would
        // otherwise be served a stale my-tenants list for up to the cache TTL (BUG-116 class).
        await SeedAsync("the-real-token");

        var permissionCache = Substitute.For<IPermissionCache>();
        var myTenantsCache = Substitute.For<IMyTenantsCache>();

        var service = new AuthService(
            CreateDbContext(),
            _jwtService,
            _tenantContext,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>(),
            myTenantsCache: myTenantsCache,
            permissionCache: permissionCache);

        (await service.AcceptInvitationAsync("the-real-token", ChosenPassword)).IsSuccess.Should().BeTrue();

        await permissionCache.Received(1).InvalidateAsync(_tenantId, _userId, Arg.Any<CancellationToken>());
        await myTenantsCache.Received(1).InvalidateAsync(_userId, Arg.Any<CancellationToken>());
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task AssertNothingProvisionedAsync()
    {
        using var db = CreateDbContext();

        (await db.UserTenants.IgnoreQueryFilters()
            .AnyAsync(ut => ut.UserId == _userId && ut.TenantId == _tenantId))
            .Should().BeFalse("a rejected redemption must not create a membership");

        // Assert the row EXISTS before asserting about it: `user?.PasswordHash.Should()` short-circuits the
        // whole chain when the user is null, so a future bug that deleted the row would make this negative
        // assertion silently pass while proving nothing.
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == _userId);
        user.Should().NotBeNull("the invitee's user row must survive a rejected redemption");
        user!.PasswordHash.Should().BeNull("a rejected redemption must not set a password");
    }

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
            Substitute.For<Hangfire.IBackgroundJobClient>());
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);
}
