// ============================================================================
// BUG-018 REGRESSION — Location audit-write cluster (Core-HR missing-audit-write).
// The LocationService create/update/deactivate operations MUST write a queryable
// row to the `audit_logs` (AuditLog) table — action OfficeLocation.Created/
// .Updated/.Deactivated — mirroring the RoleService/LeaveTypeService audit pattern.
//
// Pre-fix (git show HEAD:...LocationService.cs) writes only an ILogger line and no
// AuditLog row -> these tests FAIL. Post-fix the row is present -> they PASS.
//
// Audit rows are plain inserts, so the EF InMemory provider is sufficient. The
// harness mirrors LocationServiceTests. AuditLog has NO global query filter, so
// tenant scoping is asserted on the row's TenantId column directly.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LocationAuditWriteTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly ILogger<LocationService> _logger = Substitute.For<ILogger<LocationService>>();

    public LocationAuditWriteTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(_actorId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    private LocationService CreateService() =>
        new(TestDbContextFactory.Create(_tenantContext, _dbName), _tenantContext, _currentUser, _logger);

    private async Task<Guid> SeedLocation(string name, bool isActive = true)
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var loc = new Location
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = name,
            TimeZone = "Asia/Colombo",
            IsActive = isActive,
            IsDeleted = false,
        };
        db.Locations.Add(loc);
        await db.SaveChangesAsync();
        return loc.Id;
    }

    private async Task<List<AuditLog>> AuditRows()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        // AuditLog has no query filter; scope explicitly to this tenant.
        return await db.AuditLogs.Where(a => a.TenantId == _tenantId).ToListAsync();
    }

    private static AuditLog Single(IEnumerable<AuditLog> rows, string actionSubstring) =>
        rows.Single(a => (a.Action ?? a.EventType ?? string.Empty).Contains(actionSubstring));

    // ── BUG-018: create → OfficeLocation.Created ────────────────────────────

    [Fact]
    public async Task CreateLocation_WritesAuditRow_BUG018()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            "Head Office", "123 Main St", null, "Colombo", "Western",
            "Sri Lanka", "00100", "Asia/Colombo", null);
        result.IsSuccess.Should().BeTrue();
        var locationId = result.Value!.Id;

        var audit = Single(await AuditRows(), "OfficeLocation");

        (audit.Action ?? audit.EventType).Should().Contain("OfficeLocation");
        audit.ResourceId.Should().Be(locationId.ToString(), "the audit row must reference the created location");
        audit.TenantId.Should().Be(_tenantId, "the audit row must be tenant-scoped");
        audit.UserId.Should().Be(_actorId, "the audit row must be attributed to the acting user");
        audit.After.Should().NotBeNull("a create audit must capture an after-snapshot");
    }

    // ── BUG-018: update → OfficeLocation.Updated with before≠after ──────────

    [Fact]
    public async Task UpdateLocation_WritesAuditRowWithBeforeAfter_BUG018()
    {
        var locationId = await SeedLocation("Old HQ Name");
        var service = CreateService();

        var result = await service.UpdateAsync(
            locationId, "New HQ Name", null, null, "Kandy", "Central",
            "Sri Lanka", "20000", "Asia/Colombo", null);
        result.IsSuccess.Should().BeTrue();

        // Only the Updated op wrote a row here (the seed inserted the entity directly).
        var audit = Single(await AuditRows(), "OfficeLocation");

        audit.ResourceId.Should().Be(locationId.ToString());
        audit.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorId);
        audit.Before.Should().NotBeNull();
        audit.After.Should().NotBeNull();
        audit.Before.Should().NotBe(audit.After, "before/after must reflect the change");
        audit.Before!.Should().Contain("Old HQ Name");
        audit.After!.Should().Contain("New HQ Name");
    }

    // ── BUG-018: deactivate → OfficeLocation.Deactivated ────────────────────

    [Fact]
    public async Task DeactivateLocation_WritesAuditRow_BUG018()
    {
        var locationId = await SeedLocation("Closing Branch");
        var service = CreateService();

        var result = await service.DeactivateAsync(locationId);
        result.IsSuccess.Should().BeTrue();

        var audit = Single(await AuditRows(), "OfficeLocation");

        audit.ResourceId.Should().Be(locationId.ToString());
        audit.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorId);
        // Deactivation flips state — before and after snapshots must differ.
        audit.Before.Should().NotBe(audit.After);
    }
}
