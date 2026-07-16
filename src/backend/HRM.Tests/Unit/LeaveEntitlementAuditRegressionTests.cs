// ============================================================================
// BUG-028 (US-LV-002) — Missing audit-write regression suite.
//
// The LeaveEntitlementService previously logged rule/override mutations only via
// ILogger and wrote NO queryable audit_logs row. These tests drive the real
// service against a real (InMemory) AppDbContext and assert that every mutating
// operation appends an audit_logs row (LeaveTypeService-style AuditLogs.Add):
//   - rule create / update / delete
//   - bulk rule create
//   - override upsert (create + update) / delete
//
// Each assertion keys on the audit ROW: presence + Action substring + ResourceId
// (== the mutated entity) + tenant-scoping + actor attribution; update ops also
// assert before != after.
//
// Pre-fix (git HEAD): no AuditLogs.Add in LeaveEntitlementService -> zero rows ->
// every test FAILS. Post-fix: the rows exist -> every test PASSES.
//
// Audit rows are plain inserts with no provider-specific SQL, so InMemory is a
// faithful substrate here.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveEntitlementAuditRegressionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LeaveEntitlementService> _logger;

    private Guid _leaveTypeId;
    private Guid _departmentId;
    private Guid _employeeId;

    public LeaveEntitlementAuditRegressionTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        // Authenticated actor so the audit row is actor-attributed (UserId set, not null).
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_actorUserId);
        _currentUser.Email.Returns("admin@test.com");

        _logger = Substitute.For<ILogger<LeaveEntitlementService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveEntitlementService CreateService()
    {
        var db = CreateDbContext();
        return new(db, _tenantContext, _currentUser, _logger,
            new TenantLeaveYearResolver(db, _tenantContext));
    }

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        var dept = new Department
        {
            Id = _departmentId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Engineering",
            Code = "ENG",
            IsActive = true,
        };
        var lt = new LeaveType
        {
            Id = _leaveTypeId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Annual Leave",
            AnnualEntitlement = 14,
            AccrualFrequency = AccrualFrequency.Upfront,
            IsActive = true,
        };
        var emp = new Employee
        {
            Id = _employeeId = Guid.NewGuid(),
            TenantId = _tenantId,
            EmployeeNo = "EMP-0001",
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            DateOfJoining = new DateTime(2026, 1, 1),
            DepartmentId = dept.Id,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        };

        db.Departments.Add(dept);
        db.LeaveTypes.Add(lt);
        db.Employees.Add(emp);
        db.SaveChanges();
    }

    private UpsertLeaveEntitlementRuleRequest MakeRuleRequest(decimal entitlementDays = 20) => new()
    {
        LeaveTypeId = _leaveTypeId,
        DepartmentId = _departmentId,
        EntitlementDays = entitlementDays,
        Priority = 1,
        EffectiveFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EffectiveTo = null,
    };

    private UpsertLeaveEntitlementOverrideRequest MakeOverrideRequest(decimal days, string reason) => new()
    {
        EmployeeId = _employeeId,
        LeaveTypeId = _leaveTypeId,
        LeaveYear = 2026,
        EntitlementDays = days,
        Reason = reason,
    };

    /// <summary>Fetch all audit_logs rows targeting a given resource id (fresh read-side context).</summary>
    private List<AuditLog> AuditRowsFor(Guid resourceId)
    {
        using var db = CreateDbContext();
        return db.AuditLogs.Where(a => a.ResourceId == resourceId.ToString()).ToList();
    }

    // ── Rule create (BUG-028) ──────────────────────────────────────────────

    [Fact]
    public async Task CreateRule_WritesAuditRow_BUG028()
    {
        var created = await CreateService().CreateRuleAsync(MakeRuleRequest());
        created.IsSuccess.Should().BeTrue();
        var ruleId = created.Value!.Id;

        var rows = AuditRowsFor(ruleId);

        rows.Should().ContainSingle("a create must leave exactly one audit trail row");
        var audit = rows[0];
        audit.Action.Should().Contain("Rule", "the action names the entitlement-rule resource");
        audit.Action.Should().Contain("Created");
        audit.ResourceId.Should().Be(ruleId.ToString());
        audit.TenantId.Should().Be(_tenantId, "audit rows are tenant-scoped");
        audit.UserId.Should().Be(_actorUserId, "audit rows are attributed to the acting user");
        audit.After.Should().NotBeNullOrEmpty("a create records the new-state snapshot");
    }

    // ── Rule update (BUG-028) — before != after ────────────────────────────

    [Fact]
    public async Task UpdateRule_WritesAuditRow_BeforeDiffersAfter_BUG028()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest(entitlementDays: 20));
        var ruleId = created.Value!.Id;

        var updated = await CreateService().UpdateRuleAsync(ruleId, MakeRuleRequest(entitlementDays: 25));
        updated.IsSuccess.Should().BeTrue();

        var updateRow = AuditRowsFor(ruleId)
            .SingleOrDefault(a => a.Action != null && a.Action.Contains("Updated"));

        updateRow.Should().NotBeNull("an update must leave its own audit row");
        updateRow!.ResourceId.Should().Be(ruleId.ToString());
        updateRow.TenantId.Should().Be(_tenantId);
        updateRow.UserId.Should().Be(_actorUserId);
        updateRow.Before.Should().NotBeNullOrEmpty("an update captures the pre-mutation snapshot");
        updateRow.After.Should().NotBeNullOrEmpty("an update captures the post-mutation snapshot");
        updateRow.Before.Should().NotBe(updateRow.After, "20 -> 25 days must produce a diff");
    }

    // ── Rule delete (BUG-028) ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteRule_WritesAuditRow_BUG028()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest());
        var ruleId = created.Value!.Id;

        (await CreateService().DeleteRuleAsync(ruleId)).IsSuccess.Should().BeTrue();

        var deleteRow = AuditRowsFor(ruleId)
            .SingleOrDefault(a => a.Action != null && a.Action.Contains("Deleted"));

        deleteRow.Should().NotBeNull("a delete must leave its own audit row");
        deleteRow!.ResourceId.Should().Be(ruleId.ToString());
        deleteRow.TenantId.Should().Be(_tenantId);
        deleteRow.UserId.Should().Be(_actorUserId);
        deleteRow.Before.Should().NotBeNullOrEmpty("a delete records the removed state");
    }

    // ── Bulk rule create (BUG-028) ─────────────────────────────────────────

    [Fact]
    public async Task BulkCreateRules_WritesAuditRowPerRule_BUG028()
    {
        var result = await CreateService().BulkCreateRulesAsync(new List<UpsertLeaveEntitlementRuleRequest>
        {
            MakeRuleRequest(entitlementDays: 15),
            MakeRuleRequest(entitlementDays: 20),
        });
        result.IsSuccess.Should().BeTrue();

        foreach (var dto in result.Value!)
        {
            var rows = AuditRowsFor(dto.Id);
            rows.Should().ContainSingle($"bulk-created rule {dto.Id} must be audited");
            rows[0].Action.Should().Contain("Rule");
            rows[0].TenantId.Should().Be(_tenantId);
            rows[0].UserId.Should().Be(_actorUserId);
        }
    }

    // ── Override upsert: create branch (BUG-028) ───────────────────────────

    [Fact]
    public async Task UpsertOverride_Create_WritesAuditRow_BUG028()
    {
        var upserted = await CreateService().UpsertOverrideAsync(MakeOverrideRequest(30, "Initial"));
        upserted.IsSuccess.Should().BeTrue();
        var overrideId = upserted.Value!.Id;

        var rows = AuditRowsFor(overrideId);

        rows.Should().ContainSingle("an override create must leave exactly one audit row");
        var audit = rows[0];
        audit.Action.Should().Contain("Override", "the action names the override resource");
        audit.ResourceId.Should().Be(overrideId.ToString());
        audit.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorUserId);
        audit.After.Should().NotBeNullOrEmpty();
    }

    // ── Override upsert: update branch (BUG-028) — before != after ─────────

    [Fact]
    public async Task UpsertOverride_Update_WritesAuditRow_BeforeDiffersAfter_BUG028()
    {
        var first = await CreateService().UpsertOverrideAsync(MakeOverrideRequest(30, "Initial"));
        var overrideId = first.Value!.Id;

        var second = await CreateService().UpsertOverrideAsync(MakeOverrideRequest(35, "Updated"));
        second.IsSuccess.Should().BeTrue();
        second.Value!.Id.Should().Be(overrideId, "upsert updates the same override row");

        // The update branch is the audit row that carries a before-snapshot.
        var updateRow = AuditRowsFor(overrideId)
            .SingleOrDefault(a => !string.IsNullOrEmpty(a.Before));

        updateRow.Should().NotBeNull("the override update must leave an audited before/after row");
        updateRow!.Action.Should().Contain("Override");
        updateRow.ResourceId.Should().Be(overrideId.ToString());
        updateRow.TenantId.Should().Be(_tenantId);
        updateRow.UserId.Should().Be(_actorUserId);
        updateRow.After.Should().NotBeNullOrEmpty();
        updateRow.Before.Should().NotBe(updateRow.After, "30 -> 35 days must produce a diff");
    }

    // ── Override delete (BUG-028) ──────────────────────────────────────────

    [Fact]
    public async Task DeleteOverride_WritesAuditRow_BUG028()
    {
        var created = await CreateService().UpsertOverrideAsync(MakeOverrideRequest(30, "Test"));
        var overrideId = created.Value!.Id;

        (await CreateService().DeleteOverrideAsync(overrideId)).IsSuccess.Should().BeTrue();

        var deleteRow = AuditRowsFor(overrideId)
            .SingleOrDefault(a => a.Action != null && a.Action.Contains("Deleted"));

        deleteRow.Should().NotBeNull("an override delete must leave its own audit row");
        deleteRow!.Action.Should().Contain("Override");
        deleteRow.ResourceId.Should().Be(overrideId.ToString());
        deleteRow.TenantId.Should().Be(_tenantId);
        deleteRow.UserId.Should().Be(_actorUserId);
        deleteRow.Before.Should().NotBeNullOrEmpty("a delete records the removed state");
    }
}
