// ============================================================================
// US-ADM-005: Tenant Admin manages users and role assignments — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (MediatR pipeline + EF global query filters) in the NORMAL
// resolved-tenant context (MutableTenantContext with TenantId set, IsResolved
// true) — so tenant isolation (AC-6) is enforced by the query filters, exactly
// as in production. Mirrors AppraisalCycle/ApplicantConversion integration tests.
//
// PROVIDER: EF Core InMemory (no PostgreSQL, no Docker in the verify gate). The
// PG schema (uuid[] column, snake_case) is validated by the migrations CI job
// applying the migration to real PG.
//
// Covers: list users tenant-scoped (AC-1/AC-6), invite new + existing global
// user (AC-2), plan limit (BR-5), edit roles + audit + TenantOwner protection
// (AC-3/BR-2), deactivate + token revoke + self-deactivation + cross-tenant
// isolation (AC-4/BR-3), force password reset cross-tenant (AC-5), invitation
// expiry + resend (BR-6).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Users.Commands;
using HRM.Application.Features.Users.DTOs;
using HRM.Application.Features.Users.Queries;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class TenantUserManagementIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    // Tenant A users
    private readonly Guid _adminUserA = Guid.NewGuid();   // the acting Tenant Admin
    private readonly Guid _ownerUserA = Guid.NewGuid();   // Tenant Owner
    private readonly Guid _empUserA = Guid.NewGuid();     // plain employee membership
    private readonly Guid _sharedUser = Guid.NewGuid();   // member of BOTH A and B (isolation tests)

    // Tenant B users
    private readonly Guid _empUserB = Guid.NewGuid();

    // Roles (per tenant)
    private Guid _ownerRoleA, _adminRoleA, _managerRoleA, _hrOfficerRoleA, _employeeRoleA;
    private Guid _employeeRoleB;

    // Memberships
    private Guid _adminUtA, _ownerUtA, _empUtA, _sharedUtA;
    private Guid _empUtB, _sharedUtB;

    public TenantUserManagementIntegrationTests() => SeedData();

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

    private IMediator BuildPipeline(Guid tenantId, Guid actingUserId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(actingUserId);
        currentUser.Email.Returns("admin@acme.com");
        currentUser.TenantId.Returns(tenantId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string>());
        currentUser.Permissions.Returns(new List<string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton<IPermissionCache>(Substitute.For<IPermissionCache>());
        services.AddSingleton<IUserManagementNotificationService, RecordingNotificationService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(InviteUserCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private AppDbContext ReadDb(Guid tenantId) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new MutableTenantContext { TenantId = tenantId });

    // A shared recording stub so a test can assert an email was dispatched.
    private sealed class RecordingNotificationService : IUserManagementNotificationService
    {
        public List<UserInvitationEmailMessage> Invitations { get; } = new();
        public List<PasswordResetEmailMessage> Resets { get; } = new();

        public Task SendInvitationAsync(UserInvitationEmailMessage message, CancellationToken cancellationToken = default)
        {
            Invitations.Add(message);
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(PasswordResetEmailMessage message, CancellationToken cancellationToken = default)
        {
            Resets.Add(message);
            return Task.CompletedTask;
        }
    }

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme Corp", Status = TenantStatus.Active, MaxEmployees = null },
            new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex", Status = TenantStatus.Active, MaxEmployees = null });

        _ownerRoleA = AddRole(db, _tenantA, PermissionCatalog.BuiltInRoles.TenantOwner);
        _adminRoleA = AddRole(db, _tenantA, PermissionCatalog.BuiltInRoles.TenantAdmin);
        _managerRoleA = AddRole(db, _tenantA, PermissionCatalog.BuiltInRoles.Manager);
        _hrOfficerRoleA = AddRole(db, _tenantA, PermissionCatalog.BuiltInRoles.HROfficer);
        _employeeRoleA = AddRole(db, _tenantA, PermissionCatalog.BuiltInRoles.Employee);
        _employeeRoleB = AddRole(db, _tenantB, PermissionCatalog.BuiltInRoles.Employee);

        AddUser(db, _adminUserA, "admin@acme.com");
        AddUser(db, _ownerUserA, "owner@acme.com");
        AddUser(db, _empUserA, "emp@acme.com");
        AddUser(db, _sharedUser, "shared@multi.com");
        AddUser(db, _empUserB, "emp@globex.com");

        _adminUtA = AddMembership(db, _adminUserA, _tenantA, _adminRoleA);
        _ownerUtA = AddMembership(db, _ownerUserA, _tenantA, _ownerRoleA);
        _empUtA = AddMembership(db, _empUserA, _tenantA, _employeeRoleA);
        _sharedUtA = AddMembership(db, _sharedUser, _tenantA, _employeeRoleA);

        _empUtB = AddMembership(db, _empUserB, _tenantB, _employeeRoleB);
        _sharedUtB = AddMembership(db, _sharedUser, _tenantB, _employeeRoleB);

        // Active refresh tokens: shared user has one in EACH tenant; emp A has one in A.
        AddToken(db, _empUserA, _tenantA);
        AddToken(db, _sharedUser, _tenantA);
        AddToken(db, _sharedUser, _tenantB);

        db.SaveChanges();
    }

    private static Guid AddRole(AppDbContext db, Guid tenantId, string name)
    {
        var id = Guid.NewGuid();
        db.Roles.Add(new Role { Id = id, TenantId = tenantId, Name = name, IsBuiltIn = true });
        return id;
    }

    private static void AddUser(AppDbContext db, Guid id, string email)
        => db.Users.Add(new User { Id = id, Email = email, IsActive = true, PasswordChangedAt = DateTime.UtcNow });

    private static Guid AddMembership(AppDbContext db, Guid userId, Guid tenantId, Guid roleId)
    {
        var utId = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant
        {
            Id = utId,
            UserId = userId,
            TenantId = tenantId,
            Status = UserTenantStatus.Active,
        });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        return utId;
    }

    private static void AddToken(AppDbContext db, Guid userId, Guid tenantId)
        => db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            TokenHash = Guid.NewGuid().ToString("N"),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });

    // ── List users scoped to tenant (AC-1 / AC-6) ───────────────────────────

    [Fact]
    public async Task ListUsers_OnlyReturnsCurrentTenantUsers()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new ListTenantUsersQuery(1, 50, null, null, null));

        result.IsSuccess.Should().BeTrue();
        var emails = result.Value!.Items.Select(i => i.Email).ToList();
        emails.Should().Contain(new[] { "admin@acme.com", "owner@acme.com", "emp@acme.com", "shared@multi.com" });
        emails.Should().NotContain("emp@globex.com");
        // shared@multi.com appears once (its tenant-A membership only).
        emails.Count(e => e == "shared@multi.com").Should().Be(1);
    }

    [Fact]
    public async Task ListUsers_FromTenantB_DoesNotSeeTenantAUsers()
    {
        var mediator = BuildPipeline(_tenantB, _empUserB);

        var result = await mediator.Send(new ListTenantUsersQuery(1, 50, null, null, null));

        result.IsSuccess.Should().BeTrue();
        var emails = result.Value!.Items.Select(i => i.Email).ToList();
        emails.Should().Contain("emp@globex.com");
        emails.Should().Contain("shared@multi.com"); // its tenant-B membership
        emails.Should().NotContain("admin@acme.com");
    }

    [Fact]
    public async Task ListUsers_ResolvesRoleNames()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new ListTenantUsersQuery(1, 50, "owner@acme.com", null, null));

        var owner = result.Value!.Items.Single();
        owner.Roles.Should().ContainSingle(r => r.Name == PermissionCatalog.BuiltInRoles.TenantOwner);
    }

    // ── Invite (AC-2) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Invite_NewGlobalUser_CreatesUserAndInvitation_AndDispatchesEmail()
    {
        var provider = BuildServices(_tenantA, _adminUserA, out var notifications);
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new InviteUserCommand("newhire@acme.com", new[] { _managerRoleA }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().ContainSingle();
        result.Value.Errors.Should().BeEmpty();

        using var db = ReadDb(_tenantA);
        db.Users.Should().ContainSingle(u => u.Email == "newhire@acme.com");
        var inv = await db.UserInvitations.IgnoreQueryFilters().SingleAsync(i => i.Email == "newhire@acme.com");
        inv.Status.Should().Be(InvitationStatus.Invited);
        inv.TenantId.Should().Be(_tenantA);
        inv.InvitedRoleIds.Should().Contain(_managerRoleA);
        // 72h expiry (BR-6).
        inv.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(72), TimeSpan.FromMinutes(5));
        // No active UserTenant until acceptance.
        db.UserTenants.IgnoreQueryFilters().Any(ut => ut.User.Email == "newhire@acme.com").Should().BeFalse();

        notifications.Invitations.Should().ContainSingle(m => m.Email == "newhire@acme.com");
    }

    [Fact]
    public async Task Invite_ExistingGlobalUserFromAnotherTenant_NoDuplicateUser_NewInvitation()
    {
        // emp@globex.com exists globally (member of tenant B). Invite to tenant A → no duplicate user.
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new InviteUserCommand("emp@globex.com", new[] { _employeeRoleA }));

        result.IsSuccess.Should().BeTrue();

        using var db = ReadDb(_tenantA);
        db.Users.Count(u => u.Email == "emp@globex.com").Should().Be(1);
        var inv = await db.UserInvitations.SingleAsync(i => i.Email == "emp@globex.com");
        inv.TenantId.Should().Be(_tenantA);
    }

    [Fact]
    public async Task Invite_ExistingActiveMember_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new InviteUserCommand("emp@acme.com", new[] { _employeeRoleA }));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("already_member");
    }

    // ── Plan limit (BR-5) ────────────────────────────────────────────────────

    [Fact]
    public async Task Invite_AtPlanLimit_IsRejected()
    {
        // Tenant A has 4 active memberships. Set MaxEmployees=5 → exactly one invite allowed, the next rejected.
        using (var setup = ReadDb(_tenantA))
        {
            var t = await setup.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == _tenantA);
            t.MaxEmployees = 5;
            await setup.SaveChangesAsync();
        }

        var mediator = BuildPipeline(_tenantA, _adminUserA);

        // 4 active + 0 pending → first invite (would be 5th seat) succeeds.
        var first = await mediator.Send(new InviteUserCommand("seat5@acme.com", new[] { _employeeRoleA }));
        first.IsSuccess.Should().BeTrue();

        // 4 active + 1 pending = 5 → next invite rejected (BR-5).
        var second = await mediator.Send(new InviteUserCommand("seat6@acme.com", new[] { _employeeRoleA }));
        second.IsFailure.Should().BeTrue();
        second.Error.Should().Be("User limit reached for your plan.");
        second.ErrorCode.Should().Be("plan_limit_reached");
    }

    // ── Edit roles (AC-3 / BR-2) ─────────────────────────────────────────────

    [Fact]
    public async Task EditRoles_AssignsNewSet_AndWritesBeforeAfterAudit()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new EditUserRolesCommand(_empUtA, new[] { _managerRoleA, _hrOfficerRoleA }));

        result.IsSuccess.Should().BeTrue();

        using var db = ReadDb(_tenantA);
        var roleIds = db.UserTenantRoles.Where(utr => utr.UserTenantId == _empUtA).Select(utr => utr.RoleId).ToList();
        roleIds.Should().BeEquivalentTo(new[] { _managerRoleA, _hrOfficerRoleA });

        var audit = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.Action == "User.RolesChanged" && a.ResourceId == _empUtA.ToString())
            .OrderByDescending(a => a.CreatedAt)
            .First();
        audit.Before.Should().NotBeNull();
        audit.After.Should().NotBeNull();
        audit.Before!.Should().Contain(_employeeRoleA.ToString());
        audit.After!.Should().Contain(_managerRoleA.ToString());
    }

    [Fact]
    public async Task EditRoles_RemovingTenantOwnerFromOwner_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        // Try to replace the owner's role set with Manager only (drops Tenant Owner).
        var result = await mediator.Send(new EditUserRolesCommand(_ownerUtA, new[] { _managerRoleA }));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("owner_role_protected");

        using var db = ReadDb(_tenantA);
        db.UserTenantRoles.Where(utr => utr.UserTenantId == _ownerUtA).Select(utr => utr.RoleId)
            .Should().Contain(_ownerRoleA);
    }

    // ── Deactivate (AC-4 / BR-3) ─────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_SetsDisabled_RevokesThisTenantTokens_Audits()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new DeactivateUserCommand(_empUtA));

        result.IsSuccess.Should().BeTrue();

        using var db = ReadDb(_tenantA);
        db.UserTenants.IgnoreQueryFilters().Single(ut => ut.Id == _empUtA).Status.Should().Be(UserTenantStatus.Disabled);
        db.RefreshTokens.IgnoreQueryFilters()
            .Where(rt => rt.UserId == _empUserA && rt.TenantId == _tenantA)
            .All(rt => rt.RevokedAt != null).Should().BeTrue();
        db.AuditLogs.IgnoreQueryFilters().Any(a => a.Action == "User.Deactivated" && a.ResourceId == _empUtA.ToString())
            .Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_OwnMembership_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new DeactivateUserCommand(_adminUtA));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("self_deactivation");
    }

    [Fact]
    public async Task Deactivate_DoesNotAffectOtherTenantMembership()
    {
        // Deactivate the shared user in tenant A; their tenant-B membership + token must be untouched.
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new DeactivateUserCommand(_sharedUtA));
        result.IsSuccess.Should().BeTrue();

        using var db = ReadDb(_tenantB);
        db.UserTenants.IgnoreQueryFilters().Single(ut => ut.Id == _sharedUtB).Status.Should().Be(UserTenantStatus.Active);
        db.RefreshTokens.IgnoreQueryFilters()
            .Single(rt => rt.UserId == _sharedUser && rt.TenantId == _tenantB).RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deactivate_CrossTenantMembership_Returns404()
    {
        // Tenant A admin tries to deactivate tenant B's membership id → filtered out → 404.
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var result = await mediator.Send(new DeactivateUserCommand(_empUtB));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("not_found");
    }

    // ── Force password reset (AC-5) — cross-tenant by design ──────────────────

    [Fact]
    public async Task ForcePasswordReset_RevokesAllTokensAcrossTenants_NullsPasswordChangedAt_Audits()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        // shared user has tokens in BOTH tenant A and tenant B.
        var result = await mediator.Send(new ForcePasswordResetCommand(_sharedUtA));

        result.IsSuccess.Should().BeTrue();

        using var db = ReadDb(_tenantA);
        db.RefreshTokens.IgnoreQueryFilters()
            .Where(rt => rt.UserId == _sharedUser)
            .All(rt => rt.RevokedAt != null).Should().BeTrue();
        db.Users.Single(u => u.Id == _sharedUser).PasswordChangedAt.Should().BeNull();
        db.AuditLogs.IgnoreQueryFilters().Any(a => a.Action == "User.ForcePasswordReset").Should().BeTrue();
    }

    // ── Invitations: expiry + resend (BR-6) ──────────────────────────────────

    [Fact]
    public async Task Invitation_PastExpiry_IsReportedExpired_AndResendIssuesNewToken()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        // Create an invitation, then back-date its expiry past 72h (no sleeping).
        var invited = await mediator.Send(new InviteUserCommand("late@acme.com", new[] { _employeeRoleA }));
        var invId = invited.Value!.Created.Single().Id;

        string originalHash;
        using (var db = ReadDb(_tenantA))
        {
            var inv = await db.UserInvitations.SingleAsync(i => i.Id == invId);
            inv.ExpiresAt = DateTime.UtcNow.AddHours(-1);
            originalHash = inv.TokenHash;
            await db.SaveChangesAsync();
        }

        // List reports it expired-by-time.
        var list = await mediator.Send(new ListInvitationsQuery());
        list.Value!.Single(i => i.Id == invId).IsExpired.Should().BeTrue();

        // Resend issues a NEW token + fresh 72h expiry (BR-6).
        var resent = await mediator.Send(new ResendInvitationCommand(invId));
        resent.IsSuccess.Should().BeTrue();
        resent.Value!.IsExpired.Should().BeFalse();

        using var db2 = ReadDb(_tenantA);
        var after = await db2.UserInvitations.SingleAsync(i => i.Id == invId);
        after.TokenHash.Should().NotBe(originalHash);
        after.Status.Should().Be(InvitationStatus.Invited);
        after.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(72), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task RevokeInvitation_SetsRevoked()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);
        var invited = await mediator.Send(new InviteUserCommand("revoke-me@acme.com", new[] { _employeeRoleA }));
        var invId = invited.Value!.Created.Single().Id;

        var result = await mediator.Send(new RevokeInvitationCommand(invId));

        result.IsSuccess.Should().BeTrue();
        using var db = ReadDb(_tenantA);
        db.UserInvitations.Single(i => i.Id == invId).Status.Should().Be(InvitationStatus.Revoked);
    }

    // ── Bulk CSV invite (FR-3): valid rows succeed, invalid rows error per-row ─

    [Fact]
    public async Task BulkInvite_ValidRowsSucceed_InvalidRowsReturnedPerRow()
    {
        var mediator = BuildPipeline(_tenantA, _adminUserA);

        var rows = new List<BulkInviteRow>
        {
            new("good1@acme.com", new[] { "Manager" }),
            new("not-an-email", new[] { "Manager" }),                 // invalid email
            new("good2@acme.com", new[] { "HR Officer" }),
            new("good3@acme.com", new[] { "NoSuchRole" }),            // unknown role
            new("emp@acme.com", new[] { "Manager" }),                 // already active member
        };

        var result = await mediator.Send(new BulkInviteUsersCommand(rows));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Select(c => c.Email).Should().BeEquivalentTo(new[] { "good1@acme.com", "good2@acme.com" });
        result.Value.Errors.Should().HaveCount(3);
        result.Value.Errors.Select(e => e.Email).Should().Contain(new[] { "not-an-email", "good3@acme.com", "emp@acme.com" });
    }

    // Helper that exposes the recording notification stub for assertions.
    private ServiceProvider BuildServices(Guid tenantId, Guid actingUserId, out RecordingNotificationService notifications)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(actingUserId);
        currentUser.Email.Returns("admin@acme.com");
        currentUser.TenantId.Returns(tenantId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string>());
        currentUser.Permissions.Returns(new List<string>());

        notifications = new RecordingNotificationService();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton<IPermissionCache>(Substitute.For<IPermissionCache>());
        services.AddSingleton<IUserManagementNotificationService>(notifications);
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(InviteUserCommand).Assembly));

        return services.BuildServiceProvider();
    }
}
