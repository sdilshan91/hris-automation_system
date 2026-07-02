// ============================================================================
// BUG-041 regression: RBAC mutations (role create/update/delete, user-role
// assignment) must write a QUERYABLE audit trail (audit_log rows), not just an
// ILogger line — otherwise privilege changes are unauditable (FR-7).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RoleServiceRbacAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public RoleServiceRbacAuditTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_actorUserId);
        _currentUser.Email.Returns("admin@acme.com");
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RoleService Service() =>
        new(Db(), _tenantContext, _currentUser, Substitute.For<IPermissionCache>(),
            NullLogger<RoleService>.Instance);

    [Fact]
    public async Task CreateRole_WritesAuditLogRow()
    {
        var result = await Service().CreateRoleAsync("Approver", "Can approve", new[] { "Leave.Approve.All" });
        result.IsSuccess.Should().BeTrue();

        using var db = Db();
        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.EventType == "Role.Created");

        audit.Should().NotBeNull("role creation must be auditable (BUG-041)");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorUserId);
        audit.ResourceType.Should().Be("Role");
        audit.ResourceId.Should().Be(result.Value!.Id.ToString());
    }

    [Fact]
    public async Task DeleteRole_WritesAuditLogRow()
    {
        var created = await Service().CreateRoleAsync("Temp", null, new[] { "Reports.View" });
        created.IsSuccess.Should().BeTrue();

        var del = await Service().DeleteRoleAsync(created.Value!.Id);
        del.IsSuccess.Should().BeTrue();

        using var db = Db();
        (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Role.Deleted"))
            .Should().BeTrue("role deletion must be auditable (BUG-041)");
    }
}
