// ============================================================================
// US-ADM-003: System Admin impersonates a tenant user — service integration tests.
//
// Exercises ImpersonationService over a real AppDbContext (InMemory) in the SYSTEM/admin context, covering the
// business rules a validator unit test cannot:
//   - AC-1: a happy-path start creates an Active session, mints the impersonation token (target user/tenant/
//           session/actor), writes the "Impersonation.Started" system audit with impersonator attribution, and
//           dispatches the tenant-admin notification.
//   - AC-5/AC-6/BR-1: read-only when the initiator is System Support OR the target tenant is Suspended.
//   - BR-2: a system-tenant user cannot be impersonated.
//   - BR-3: only one active session per impersonator.
//   - BR-5: a terminated tenant cannot be impersonated.
//   - precondition: the target must have an ACTIVE membership in the target tenant.
//   - AC-3: ending a session sets it Ended and writes the "Impersonation.Ended" audit.
//   - AC-1 picker: ListTargets returns active members and excludes system users (BR-2).
//
// PROVIDER: InMemory — same rationale as the sibling integration tests (no Docker/Postgres in the verify gate).
// IJwtService is substituted (no signing key needed); we assert the service calls it with the right identity.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Impersonation.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class ImpersonationIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _adminId = Guid.NewGuid();

    private sealed class SystemTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "admin";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            new SystemTenantContext());

    private (ImpersonationService Service, IJwtService Jwt, IImpersonationNotificationService Notify) Service(
        bool actorIsSupport = false)
    {
        var jwt = Substitute.For<IJwtService>();
        jwt.GenerateImpersonationToken(
            Arg.Any<User>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(),
            Arg.Any<IEnumerable<string>>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<DateTime>())
            .Returns("fake.jwt.token");

        var notify = Substitute.For<IImpersonationNotificationService>();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(_adminId);
        user.Email.Returns("sysadmin@platform.test");
        user.IsAuthenticated.Returns(true);
        user.IsImpersonating.Returns(false);
        user.Roles.Returns(actorIsSupport
            ? new[] { PermissionCatalog.SystemRoles.SystemSupport }
            : Array.Empty<string>());

        var config = new ConfigurationBuilder().Build(); // defaults: system tenant "platform", base domain

        var service = new ImpersonationService(
            Db(), user, jwt, notify, config, NullLogger<ImpersonationService>.Instance);
        return (service, jwt, notify);
    }

    /// <summary>Seeds a tenant + a target user with an ACTIVE membership (and an optional role). Returns ids.</summary>
    private async Task<(Guid TenantId, Guid UserId)> SeedTenantWithUserAsync(
        string subdomain, TenantStatus status, string email = "user@acme.test", string? roleName = null)
    {
        var tenantId = BaseEntity.NewUuidV7();
        var userId = BaseEntity.NewUuidV7();
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = status,
            PlanId = "starter", CreatedAt = DateTime.UtcNow,
        });
        db.Users.Add(new User { Id = userId, Email = email, IsActive = true, CreatedAt = DateTime.UtcNow });
        var ut = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(), UserId = userId, TenantId = tenantId,
            Status = UserTenantStatus.Active, CreatedAt = DateTime.UtcNow,
        };
        db.UserTenants.Add(ut);
        if (roleName is not null)
        {
            var role = new Role
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = roleName,
                IsBuiltIn = true, CreatedAt = DateTime.UtcNow,
            };
            db.Roles.Add(role);
            db.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = ut.Id, RoleId = role.Id, AssignedAt = DateTime.UtcNow, AssignedBy = "seed",
            });
        }
        await db.SaveChangesAsync();
        return (tenantId, userId);
    }

    private static StartImpersonationInput Input(Guid tenantId, Guid userId,
        string reason = "Investigating a reported payroll discrepancy for support.")
        => new(userId, tenantId, reason);

    // ── AC-1: happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task Start_HappyPath_CreatesSessionTokenAuditAndNotification()
    {
        var (tenantId, userId) = await SeedTenantWithUserAsync("acme", TenantStatus.Active, roleName: "Employee");
        var (service, jwt, notify) = Service();

        var result = await service.StartAsync(Input(tenantId, userId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.IsReadOnly.Should().BeFalse();
        result.Value.Token.Should().Be("fake.jwt.token");
        result.Value.RedirectUrl.Should().Contain("acme.");

        using var db = Db();
        var session = await db.ImpersonationSessions.SingleAsync(s => s.Id == result.Value.SessionId);
        session.Status.Should().Be(ImpersonationSessionStatus.Active);
        session.ImpersonatorUserId.Should().Be(_adminId);
        session.TargetUserId.Should().Be(userId);

        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.Action == "Impersonation.Started" && a.ImpersonationSessionId == session.Id);
        audit.ImpersonatorUserId.Should().Be(_adminId);
        audit.IsImpersonationAction.Should().BeTrue();

        jwt.Received(1).GenerateImpersonationToken(
            Arg.Is<User>(u => u.Id == userId), tenantId, Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(),
            Arg.Any<IEnumerable<string>>(), session.Id, _adminId, Arg.Any<string>(), false, Arg.Any<DateTime>());
        await notify.Received(1).NotifyImpersonationStartedAsync(
            Arg.Is<ImpersonationStartedNotification>(n => n.SessionId == session.Id && n.TargetUserId == userId),
            Arg.Any<CancellationToken>());
    }

    // ── AC-5: suspended tenant → read-only ──────────────────────────────────

    [Fact]
    public async Task Start_SuspendedTenant_IsReadOnly()
    {
        var (tenantId, userId) = await SeedTenantWithUserAsync("susp", TenantStatus.Suspended);
        var (service, _, _) = Service();

        var result = await service.StartAsync(Input(tenantId, userId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.IsReadOnly.Should().BeTrue();
    }

    // ── AC-6: System Support initiator → read-only ──────────────────────────

    [Fact]
    public async Task Start_SystemSupportInitiator_IsReadOnly()
    {
        var (tenantId, userId) = await SeedTenantWithUserAsync("acme", TenantStatus.Active);
        var (service, _, _) = Service(actorIsSupport: true);

        var result = await service.StartAsync(Input(tenantId, userId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.IsReadOnly.Should().BeTrue();
    }

    // ── BR-5: terminated tenant rejected ────────────────────────────────────

    [Fact]
    public async Task Start_TerminatedTenant_IsRejected()
    {
        var (tenantId, userId) = await SeedTenantWithUserAsync("dead", TenantStatus.Terminated);
        var (service, _, _) = Service();

        var result = await service.StartAsync(Input(tenantId, userId));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("tenant_terminated");
    }

    // ── precondition: no active membership ──────────────────────────────────

    [Fact]
    public async Task Start_NoActiveMembership_IsRejected()
    {
        var (tenantId, _) = await SeedTenantWithUserAsync("acme", TenantStatus.Active);
        var (service, _, _) = Service();

        var result = await service.StartAsync(Input(tenantId, Guid.NewGuid())); // random non-member user

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("membership_not_found");
    }

    // ── BR-3: one active session per impersonator ───────────────────────────

    [Fact]
    public async Task Start_SecondConcurrentSession_IsRejected()
    {
        var (tenantId, userId) = await SeedTenantWithUserAsync("acme", TenantStatus.Active);
        var (service1, _, _) = Service();
        (await service1.StartAsync(Input(tenantId, userId))).IsSuccess.Should().BeTrue();

        var (service2, _, _) = Service(); // same _adminId actor
        var second = await service2.StartAsync(Input(tenantId, userId));

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("session_active");
    }

    // ── BR-2: cannot impersonate a system-tenant user ───────────────────────

    [Fact]
    public async Task Start_SystemTenantUser_IsRejected()
    {
        // Seed the platform/system tenant and a user who is a member of it, then also a member of a normal tenant.
        var (systemTenantId, systemUserId) =
            await SeedTenantWithUserAsync("platform", TenantStatus.Active, email: "admin@platform.test");
        var normalTenantId = BaseEntity.NewUuidV7();
        using (var db = Db())
        {
            db.Tenants.Add(new Tenant
            {
                Id = normalTenantId, Subdomain = "acme", Name = "acme", Status = TenantStatus.Active,
                PlanId = "starter", CreatedAt = DateTime.UtcNow,
            });
            db.UserTenants.Add(new UserTenant
            {
                Id = BaseEntity.NewUuidV7(), UserId = systemUserId, TenantId = normalTenantId,
                Status = UserTenantStatus.Active, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (service, _, _) = Service();
        var result = await service.StartAsync(Input(normalTenantId, systemUserId));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("target_is_system");
        _ = systemTenantId; // platform tenant existence is what makes the system-user check fire
    }

    // ── AC-3: end session ───────────────────────────────────────────────────

    [Fact]
    public async Task End_ActiveSession_SetsEndedAndWritesAudit()
    {
        var (tenantId, userId) = await SeedTenantWithUserAsync("acme", TenantStatus.Active);
        var (service, _, _) = Service();
        var started = await service.StartAsync(Input(tenantId, userId));
        var sessionId = started.Value!.SessionId;

        var ended = await service.EndAsync(sessionId);

        ended.IsSuccess.Should().BeTrue(ended.Error);
        ended.Value!.Status.Should().Be(nameof(ImpersonationSessionStatus.Ended));

        using var db = Db();
        (await db.ImpersonationSessions.SingleAsync(s => s.Id == sessionId)).Status
            .Should().Be(ImpersonationSessionStatus.Ended);
        (await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == "Impersonation.Ended" && a.ImpersonationSessionId == sessionId))
            .Should().BeTrue();
    }

    // ── AC-1 picker: list targets excludes system users ─────────────────────

    [Fact]
    public async Task ListTargets_ReturnsActiveMembers_ExcludesSystemUsers()
    {
        var (tenantId, memberId) = await SeedTenantWithUserAsync("acme", TenantStatus.Active, email: "emp@acme.test");
        // A platform/system user who is ALSO a member of this tenant must be excluded (BR-2).
        var (_, systemUserId) = await SeedTenantWithUserAsync("platform", TenantStatus.Active, email: "ops@platform.test");
        using (var db = Db())
        {
            db.UserTenants.Add(new UserTenant
            {
                Id = BaseEntity.NewUuidV7(), UserId = systemUserId, TenantId = tenantId,
                Status = UserTenantStatus.Active, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (service, _, _) = Service();
        var result = await service.ListTargetsAsync(tenantId);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Select(t => t.UserId).Should().Contain(memberId).And.NotContain(systemUserId);
    }
}
