// ============================================================================
// BUG-031 (US-LV-007) — Missing audit-write regression suite.
//
// The HolidayService previously logged calendar mutations only via ILogger and
// wrote NO queryable audit_logs row. These tests drive the real service against a
// real (InMemory) AppDbContext and assert that every mutating operation appends an
// audit_logs row (LeaveTypeService-style AuditLogs.Add):
//   - holiday create / update / deactivate  -> Holiday.Created / .Updated / .Deactivated
//   - CSV import                             -> a Holiday.Imported summary row
//
// Each per-entity assertion keys on the audit ROW: presence + Action substring +
// ResourceId (== the mutated holiday) + tenant-scoping + actor attribution; the
// update op also asserts before != after. The import assertion keys on a summary
// row (action substring + tenant + actor) rather than a single entity id.
//
// Pre-fix (git HEAD): no AuditLogs.Add in HolidayService -> zero rows -> every
// test FAILS. Post-fix: the rows exist -> every test PASSES.
//
// Audit rows are plain inserts, so InMemory is a faithful substrate here.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Holidays.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class HolidayAuditRegressionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<HolidayService> _logger;

    public HolidayAuditRegressionTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        // Authenticated actor so the audit row is actor-attributed (UserId set, not null).
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_actorUserId);
        _currentUser.Email.Returns("hr@test.com");

        _logger = Substitute.For<ILogger<HolidayService>>();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private HolidayService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser, _logger);

    private static CreateHolidayRequest Req(DateOnly date, string name = "Holiday", string type = "Public") => new()
    {
        Name = name,
        Date = date,
        Type = type,
    };

    /// <summary>Fetch all audit_logs rows targeting a given resource id (fresh read-side context).</summary>
    private List<AuditLog> AuditRowsFor(Guid resourceId)
    {
        using var db = CreateDbContext();
        return db.AuditLogs.Where(a => a.ResourceId == resourceId.ToString()).ToList();
    }

    /// <summary>Fetch all audit_logs rows whose Action contains the given fragment.</summary>
    private List<AuditLog> AuditRowsWithAction(string actionFragment)
    {
        using var db = CreateDbContext();
        return db.AuditLogs.Where(a => a.Action != null && a.Action.Contains(actionFragment)).ToList();
    }

    // ── Holiday create (BUG-031) ───────────────────────────────────────────

    [Fact]
    public async Task CreateHoliday_WritesAuditRow_BUG031()
    {
        var created = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "New Year's Day"));
        created.IsSuccess.Should().BeTrue();
        var holidayId = created.Value!.Id;

        var rows = AuditRowsFor(holidayId);

        rows.Should().ContainSingle("a holiday create must leave exactly one audit row");
        var audit = rows[0];
        audit.Action.Should().Contain("Holiday", "the action names the holiday resource");
        audit.Action.Should().Contain("Created");
        audit.ResourceId.Should().Be(holidayId.ToString());
        audit.TenantId.Should().Be(_tenantId, "audit rows are tenant-scoped");
        audit.UserId.Should().Be(_actorUserId, "audit rows are attributed to the acting user");
        audit.After.Should().NotBeNullOrEmpty("a create records the new-state snapshot");
    }

    // ── Holiday update (BUG-031) — before != after ─────────────────────────

    [Fact]
    public async Task UpdateHoliday_WritesAuditRow_BeforeDiffersAfter_BUG031()
    {
        var created = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "Old Name"));
        var holidayId = created.Value!.Id;

        var updated = await CreateService().UpdateAsync(holidayId, new UpdateHolidayRequest
        {
            Name = "New Name",
            Date = new DateOnly(2026, 1, 2),
            Type = "Restricted",
        });
        updated.IsSuccess.Should().BeTrue();

        var updateRow = AuditRowsFor(holidayId)
            .SingleOrDefault(a => a.Action != null && a.Action.Contains("Updated"));

        updateRow.Should().NotBeNull("a holiday update must leave its own audit row");
        updateRow!.ResourceId.Should().Be(holidayId.ToString());
        updateRow.TenantId.Should().Be(_tenantId);
        updateRow.UserId.Should().Be(_actorUserId);
        updateRow.Before.Should().NotBeNullOrEmpty("an update captures the pre-mutation snapshot");
        updateRow.After.Should().NotBeNullOrEmpty("an update captures the post-mutation snapshot");
        updateRow.Before.Should().NotBe(updateRow.After, "name/date/type change must produce a diff");
    }

    // ── Holiday deactivate (BUG-031) ───────────────────────────────────────

    [Fact]
    public async Task DeactivateHoliday_WritesAuditRow_BUG031()
    {
        var created = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1)));
        var holidayId = created.Value!.Id;

        (await CreateService().DeactivateAsync(holidayId)).IsSuccess.Should().BeTrue();

        var deactivateRow = AuditRowsFor(holidayId)
            .SingleOrDefault(a => a.Action != null && a.Action.Contains("Deactivated"));

        deactivateRow.Should().NotBeNull("a holiday deactivate must leave its own audit row");
        deactivateRow!.Action.Should().Contain("Holiday");
        deactivateRow.ResourceId.Should().Be(holidayId.ToString());
        deactivateRow.TenantId.Should().Be(_tenantId);
        deactivateRow.UserId.Should().Be(_actorUserId);
    }

    // ── CSV import (BUG-031) — summary row ─────────────────────────────────

    [Fact]
    public async Task ImportHolidays_WritesSummaryAuditRow_BUG031()
    {
        const string csv =
            "name,date,type\n" +
            "New Year's Day,2026-01-01,Public\n" +
            "Independence Day,2026-07-04,Public\n" +
            "Christmas Day,2026-12-25,Public\n";

        var result = await CreateService().ImportCsvAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), "holidays.csv");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().Be(3);

        var importRows = AuditRowsWithAction("Import");

        importRows.Should().ContainSingle("a CSV import must leave exactly one summary audit row");
        var summary = importRows[0];
        summary.Action.Should().Contain("Holiday", "the summary names the holiday resource");
        summary.TenantId.Should().Be(_tenantId, "the import summary is tenant-scoped");
        summary.UserId.Should().Be(_actorUserId, "the import summary is attributed to the acting user");
    }
}
