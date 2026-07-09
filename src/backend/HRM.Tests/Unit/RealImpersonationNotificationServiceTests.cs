// ============================================================================
// US-NTF-006 Phase 2a — RealImpersonationNotificationService (US-ADM-003 AC-4/FR-5).
// Resolves the TARGET tenant's admin users (Tenant Owner / Tenant Admin, active
// membership) and dispatches the mandatory "impersonation_started" security
// notification (email + in-app) to EACH admin only — never the non-admin members,
// and never throwing on a dispatch failure.
//
// Provider: EF Core InMemory AppDbContext (seeded users/memberships/roles). The
// INotificationDispatcher is substituted (NSubstitute) so we assert Received per
// recipient with EventKey=="impersonation_started" rather than delivery rows.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealImpersonationNotificationServiceTests
{
    private const string EventKey = "impersonation_started";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    private RealImpersonationNotificationService CreateService() =>
        new(TestDbContextFactory.Create(_tenantId, _dbName), _dispatcher,
            NullLogger<RealImpersonationNotificationService>.Instance);

    // Seeds a user + active membership in the target tenant, optionally granting an admin role.
    private Guid SeedMember(string roleName)
    {
        var userId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        Seed(db =>
        {
            db.Users.Add(new User { Id = userId, Email = $"{userId:N}@acme.test" });
            db.UserTenants.Add(new UserTenant
            {
                Id = membershipId, UserId = userId, TenantId = _tenantId, Status = UserTenantStatus.Active,
            });

            var role = db.Roles.SingleOrDefault(r => r.TenantId == _tenantId && r.Name == roleName);
            Guid roleId;
            if (role is null)
            {
                roleId = Guid.NewGuid();
                db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = roleName, IsBuiltIn = true });
            }
            else
            {
                roleId = role.Id;
            }
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = membershipId, RoleId = roleId });
        });
        return userId;
    }

    private void Seed(Action<AppDbContext> seed)
    {
        using var db = TestDbContextFactory.Create(_tenantId, _dbName);
        seed(db);
        db.SaveChanges();
    }

    private ImpersonationStartedNotification Notification() =>
        new(
            SessionId: Guid.NewGuid(),
            TargetTenantId: _tenantId,
            TargetTenantName: "Acme Corporation",
            ImpersonatorUserId: Guid.NewGuid(),
            ImpersonatorEmail: "support@platform.test",
            TargetUserId: Guid.NewGuid(),
            TargetUserEmail: "jane@acme.test",
            Reason: "Investigating ticket #4821",
            IsReadOnly: true,
            StartedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddHours(1));

    // ── Dispatches email + in-app to EACH tenant admin only (not the non-admin) ──────────────
    [Fact]
    public async Task NotifyImpersonationStarted_DispatchesEmailAndInApp_ToEachAdminOnly()
    {
        var ownerId = SeedMember(PermissionCatalog.BuiltInRoles.TenantOwner);
        var adminId = SeedMember(PermissionCatalog.BuiltInRoles.TenantAdmin);
        var employeeId = SeedMember(PermissionCatalog.BuiltInRoles.Employee);

        await CreateService().NotifyImpersonationStartedAsync(Notification());

        // Both admins receive email + in-app for the impersonation_started security event.
        foreach (var adminUserId in new[] { ownerId, adminId })
        {
            await _dispatcher.Received(1).SendEmailAsync(
                Arg.Is<NotificationRequest>(r =>
                    r.RecipientUserId == adminUserId && r.EventKey == EventKey && r.TenantId == _tenantId),
                Arg.Any<CancellationToken>());
            await _dispatcher.Received(1).SendInAppAsync(
                Arg.Is<NotificationRequest>(r =>
                    r.RecipientUserId == adminUserId && r.EventKey == EventKey && r.TenantId == _tenantId),
                Arg.Any<CancellationToken>());
        }

        // Exactly the two admins — the non-admin employee is never notified.
        await _dispatcher.Received(2).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().SendEmailAsync(
            Arg.Is<NotificationRequest>(r => r.RecipientUserId == employeeId), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().SendInAppAsync(
            Arg.Is<NotificationRequest>(r => r.RecipientUserId == employeeId), Arg.Any<CancellationToken>());
    }

    // ── No tenant admins → nothing dispatched, and no throw ─────────────────────────────────
    [Fact]
    public async Task NotifyImpersonationStarted_WhenNoAdmins_DispatchesNothing_AndDoesNotThrow()
    {
        SeedMember(PermissionCatalog.BuiltInRoles.Employee); // only a non-admin member exists

        var act = async () => await CreateService().NotifyImpersonationStartedAsync(Notification());

        await act.Should().NotThrowAsync();
        await _dispatcher.DidNotReceive().SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().SendInAppAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Dispatcher failure never propagates (session already committed) ─────────────────────
    [Fact]
    public async Task NotifyImpersonationStarted_WhenDispatcherThrows_DoesNotThrow()
    {
        SeedMember(PermissionCatalog.BuiltInRoles.TenantOwner);
        _dispatcher
            .When(d => d.SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("smtp down"));

        var act = async () => await CreateService().NotifyImpersonationStartedAsync(Notification());

        await act.Should().NotThrowAsync();
    }
}
