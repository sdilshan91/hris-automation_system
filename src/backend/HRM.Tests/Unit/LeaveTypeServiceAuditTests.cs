// ============================================================================
// BUG-025 regression (HIGH, Leave Management, US-LV-001, AC-2 / NFR-3):
// LeaveTypeService create/update/deactivate/reactivate/reorder currently succeed
// but write ZERO rows to audit_logs — only a Serilog line. AC-2 / NFR-3 require a
// PERSISTED before/after audit trail for every mutating leave-type operation.
//
// These tests assert a QUERYABLE audit_logs row is written per mutating op (with
// before/after JSON on update). They mirror RoleServiceRbacAuditTests (BUG-041).
//
//  - Pre-fix : LeaveTypeService writes no AuditLog rows → the "row exists"
//              assertions FAIL (audit query returns null).
//  - Post-fix: the service appends an audit row per mutating op → they PASS.
//
// Audit rows are ordinary table inserts (no unique-index / DB-race dependency),
// so the EF Core InMemory provider is acceptable here — same as the sibling
// RoleService audit tests.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveTypes.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveTypeServiceAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public LeaveTypeServiceAuditTests()
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

    private LeaveTypeService Service() =>
        new(Db(), _tenantContext, _currentUser, NullLogger<LeaveTypeService>.Instance);

    private static CreateLeaveTypeRequest CreateRequest(string name = "Annual Leave", decimal entitlement = 14) => new()
    {
        Name = name,
        Code = "AL",
        Color = "#4CAF50",
        Description = "Paid annual leave",
        AnnualEntitlement = entitlement,
        AccrualFrequency = "Monthly",
        ProbationEligible = false,
        DocumentsRequired = false,
        Encashable = false,
        HalfDayAllowed = true,
        HourlyAllowed = false,
        Gender = "All",
        NegativeBalanceAllowed = false,
    };

    /// <summary>
    /// Seeds a leave type directly (no audit row — a raw insert) so an update/deactivate
    /// under test produces exactly ONE audit row for that resource id.
    /// </summary>
    private async Task<Guid> SeedLeaveType(decimal entitlement = 14, bool isActive = true)
    {
        using var db = Db();
        var lt = new LeaveType
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = "Annual Leave",
            Code = "AL",
            AnnualEntitlement = entitlement,
            AccrualFrequency = AccrualFrequency.Monthly,
            Gender = LeaveTypeGender.All,
            DisplayOrder = 1,
            IsActive = isActive,
            IsDeleted = false,
        };
        db.LeaveTypes.Add(lt);
        await db.SaveChangesAsync();
        return lt.Id;
    }

    /// <summary>
    /// Fetches the single audit row written for a given leave-type id. Uses IgnoreQueryFilters
    /// (mirrors RoleServiceRbacAuditTests) and asserts tenant scoping explicitly below.
    /// </summary>
    private async Task<AuditLog?> AuditRowFor(Guid leaveTypeId)
    {
        using var db = Db();
        return await db.AuditLogs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.ResourceId == leaveTypeId.ToString());
    }

    // -----------------------------------------------------------------------
    // AC-2 / NFR-3: CREATE must persist a queryable audit row (BUG-025).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CreateLeaveType_WritesAuditRow()
    {
        var result = await Service().CreateAsync(CreateRequest());
        result.IsSuccess.Should().BeTrue();

        var audit = await AuditRowFor(result.Value!.Id);

        audit.Should().NotBeNull(
            "creating a leave type must persist a queryable audit_logs row, not just a log line (BUG-025, AC-2/NFR-3)");
        audit!.TenantId.Should().Be(_tenantId, "audit rows must be tenant-scoped");
        audit.UserId.Should().Be(_actorUserId, "the acting user must be attributed");
        audit.ResourceId.Should().Be(result.Value!.Id.ToString());
        // Action name convention may be "leave_type.created" / "LeaveType.Created"; match either.
        (audit.Action ?? audit.EventType).Should().NotBeNullOrWhiteSpace();
        (audit.Action ?? audit.EventType).ToLowerInvariant()
            .Should().Contain("creat", "the audit action must identify this as a create");
        (audit.ResourceType ?? string.Empty).ToLowerInvariant()
            .Should().Contain("leave", "the audit row must identify the leave-type resource");
    }

    // -----------------------------------------------------------------------
    // AC-2 / NFR-3: UPDATE must persist a BEFORE/AFTER audit snapshot (BUG-025).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task UpdateLeaveType_WritesBeforeAfterAuditRow()
    {
        var id = await SeedLeaveType(entitlement: 14);

        var result = await Service().UpdateAsync(id, new UpdateLeaveTypeRequest
        {
            Name = "Annual Leave",
            AnnualEntitlement = 25, // 14 -> 25: the change that before/after must capture
            AccrualFrequency = "Monthly",
            Gender = "All",
        });
        result.IsSuccess.Should().BeTrue();

        var audit = await AuditRowFor(id);

        audit.Should().NotBeNull(
            "updating a leave type must persist a queryable audit_logs row (BUG-025, AC-2/NFR-3)");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorUserId);
        audit.ResourceId.Should().Be(id.ToString());
        (audit.Action ?? audit.EventType).ToLowerInvariant()
            .Should().Contain("updat", "the audit action must identify this as an update");

        // The core of BUG-025: the before/after JSON snapshots must reflect the change,
        // not be null/empty.
        audit.Before.Should().NotBeNullOrWhiteSpace("the before snapshot must capture the prior state");
        audit.After.Should().NotBeNullOrWhiteSpace("the after snapshot must capture the new state");
        audit.Before.Should().NotBe(audit.After, "before and after must differ for a real change");
        audit.Before!.Should().Contain("14", "the before snapshot must show the prior entitlement (14)");
        audit.After!.Should().Contain("25", "the after snapshot must show the new entitlement (25)");
    }

    // -----------------------------------------------------------------------
    // AC-4 / FR-5 / NFR-3: DEACTIVATE must persist a queryable audit row (BUG-025).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task DeactivateLeaveType_WritesAuditRow()
    {
        var id = await SeedLeaveType(isActive: true);

        var result = await Service().DeactivateAsync(id);
        result.IsSuccess.Should().BeTrue();

        var audit = await AuditRowFor(id);

        audit.Should().NotBeNull(
            "deactivating a leave type must persist a queryable audit_logs row (BUG-025, AC-4/NFR-3)");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorUserId);
        audit.ResourceId.Should().Be(id.ToString());
        (audit.Action ?? audit.EventType).ToLowerInvariant()
            .Should().Contain("deactivat", "the audit action must identify this as a deactivation");
    }

    // -----------------------------------------------------------------------
    // NFR-3: REACTIVATE must persist a queryable audit row (BUG-025).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ReactivateLeaveType_WritesAuditRow()
    {
        var id = await SeedLeaveType(isActive: false);

        var result = await Service().ReactivateAsync(id);
        result.IsSuccess.Should().BeTrue();

        var audit = await AuditRowFor(id);

        audit.Should().NotBeNull(
            "reactivating a leave type must persist a queryable audit_logs row (BUG-025, NFR-3)");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorUserId);
        audit.ResourceId.Should().Be(id.ToString());
        (audit.Action ?? audit.EventType).ToLowerInvariant()
            .Should().Contain("reactivat", "the audit action must identify this as a reactivation");
    }
}
