// ============================================================================
// ISSUE-004 (US-ADM-005 FR-1) — regression.
//
// GET /api/v1/tenant/users?status=Invited must NOT silently return the full user
// list. UserTenantStatus is { Active, Disabled, Suspended } — there is no "Invited"
// membership state (invitees live only in user_invitations). PRE-FIX the status
// filter was gated on Enum.TryParse<UserTenantStatus>(...): an unparseable value
// (including "Invited" and any garbage) FELL THROUGH to "no predicate" → the whole
// active list was returned with 200. POST-FIX an unrecognized status yields an empty
// page (Where(_ => false)) instead of silently widening to all.
//
// Drives the REAL UserManagementService over a real AppDbContext (InMemory) in the
// normal resolved-tenant context. Seeds MIXED-status memberships (Active + Disabled,
// none "Invited") so the assertion keys on the param CHANGING the result set:
//   - status="Invited" → 0 rows      (PRE-FIX: all 3 → RED)
//   - status="Active"  → the 2 active (control: the valid filter still works)
//   - status=null      → all 3       (control: proves the seed is non-empty)
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class UserListStatusFilterIssue004Tests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;

    public UserListStatusFilterIssue004Tests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
        _tenantContext.Subdomain.Returns("acme");
        SeedMixedStatusMemberships();
    }

    private UserManagementService Service() => new(
        TestDbContextFactory.Create(_tenantContext, _dbName),
        _tenantContext,
        Substitute.For<ICurrentUser>(),
        Substitute.For<IPermissionCache>(),
        Substitute.For<IUserManagementNotificationService>(),
        NullLogger<UserManagementService>.Instance);

    private void SeedMixedStatusMemberships()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);

        // Two Active + one Disabled member of THIS tenant. Deliberately NO "Invited" — there is no such
        // membership state, which is the whole point of the finding.
        AddMember(db, "active1@acme.test", UserTenantStatus.Active);
        AddMember(db, "active2@acme.test", UserTenantStatus.Active);
        AddMember(db, "disabled@acme.test", UserTenantStatus.Disabled);

        db.SaveChanges();
    }

    private void AddMember(AppDbContext db, string email, UserTenantStatus status)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = email, IsActive = true, PasswordChangedAt = DateTime.UtcNow });
        db.UserTenants.Add(new UserTenant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = _tenantId,
            Status = status,
        });
    }

    // ── Focused regression: status=Invited must NOT return all users ────────────

    [Fact]
    public async Task ListUsers_StatusInvited_ReturnsEmptyNotAll_ISSUE004()
    {
        var result = await Service().ListUsersAsync(new ListTenantUsersInput(1, 50, null, "Invited", null));

        result.IsSuccess.Should().BeTrue(result.Error);
        // PRE-FIX: "Invited" fails to parse → filter dropped → all 3 members returned → this fails.
        result.Value!.TotalCount.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
    }

    // ── Control: a valid status still filters (and the seed really has data) ─────

    [Fact]
    public async Task ListUsers_StatusActive_ReturnsOnlyActiveMembers_ISSUE004()
    {
        var active = await Service().ListUsersAsync(new ListTenantUsersInput(1, 50, null, "Active", null));
        active.Value!.Items.Select(i => i.Email)
            .Should().BeEquivalentTo(new[] { "active1@acme.test", "active2@acme.test" });

        var all = await Service().ListUsersAsync(new ListTenantUsersInput(1, 50, null, null, null));
        all.Value!.TotalCount.Should().Be(3);
    }
}
