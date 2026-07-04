// ============================================================================
// ISSUE-097 (US-PRF-001) — Goal create/update/delete MUST write an audit_logs row.
//
// Regression tests for the "missing-audit-write" cluster. GoalService today logs to
// Serilog and relies on the AuditInterceptor to stamp CreatedBy/UpdatedBy, but writes
// NO queryable audit_logs row for goal create/update/delete (FR-6). The fix adds
// "Goal.Created" / "Goal.Updated" / "Goal.Deleted" rows (LeaveTypeService.AddLeaveTypeAudit
// pattern): tenant + actor from context, ResourceId == the goal, before/after JSON on update.
//
// Drives the REAL GoalService through the InMemory harness. The acting user holds the HR
// override (Performance.SetGoal.All) so authorization passes without a manager linkage; the
// cycle's goal-setting window is open. FAILS pre-fix (no row) / PASSES post-fix.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class GoalServiceAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();

    public GoalServiceAuditTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Email.Returns("hr@acme.com");
        _currentUser.Permissions.Returns(new[] { PermissionCatalog.Performance.SetGoalAll });
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private GoalService Service() => new(
        Db(),
        _tenantContext,
        _currentUser,
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<GoalService>>());

    private void Seed()
    {
        using var db = Db();
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Employees.Add(new Employee
        {
            Id = _employeeId, TenantId = _tenantId, UserId = Guid.NewGuid(),
            EmployeeNo = "EMP-0001", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-5), GoalSettingEnd = now.AddDays(5), // window OPEN
            SelfAssessmentStart = now.AddDays(10), SelfAssessmentEnd = now.AddDays(20),
            RatingScaleMax = 5, SelfWeightPercent = 30, IsDeleted = false,
        });
        db.SaveChanges();
    }

    private GoalInput Input(string title = "Ship the API", int weight = 60)
        => new(_cycleId, _employeeId, title, "desc", GoalCategory.Kpi, weight, "100%", "%",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null);

    private async Task<List<AuditLog>> AuditsFor(Guid goalId)
    {
        using var db = Db();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.ResourceId == goalId.ToString())
            .ToListAsync();
    }

    private static bool ActionContains(AuditLog a, string s)
        => (a.Action?.Contains(s) ?? false) || (a.EventType?.Contains(s) ?? false);

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGoal_WritesAuditRow_ISSUE097()
    {
        Seed();

        var result = await Service().CreateAsync(Input());
        result.IsSuccess.Should().BeTrue();
        var goalId = result.Value!.Id;

        var audits = await AuditsFor(goalId);

        audits.Should().ContainSingle(a => ActionContains(a, "Created"),
            "goal creation must write a Goal.Created audit row (ISSUE-097)");
        var row = audits.Single(a => ActionContains(a, "Created"));
        ActionContains(row, "Goal").Should().BeTrue();
        row.ResourceId.Should().Be(goalId.ToString());
        row.TenantId.Should().Be(_tenantId);
        row.UserId.Should().Be(_userId);
    }

    // ── Update (before ≠ after) ───────────────────────────────────────

    [Fact]
    public async Task UpdateGoal_WritesAuditRow_WithBeforeAfter_ISSUE097()
    {
        Seed();
        var created = await Service().CreateAsync(Input(title: "Original title", weight: 40));
        created.IsSuccess.Should().BeTrue();
        var goalId = created.Value!.Id;

        var updated = await Service().UpdateAsync(goalId, Input(title: "Revised title", weight: 55));
        updated.IsSuccess.Should().BeTrue();

        var audits = await AuditsFor(goalId);

        audits.Should().Contain(a => ActionContains(a, "Updated"),
            "goal update must write a Goal.Updated audit row (ISSUE-097)");
        var row = audits.Single(a => ActionContains(a, "Updated"));
        row.TenantId.Should().Be(_tenantId);
        row.UserId.Should().Be(_userId);

        // An update audit must capture the change: before ≠ after (both populated).
        row.Before.Should().NotBeNullOrEmpty();
        row.After.Should().NotBeNullOrEmpty();
        row.After.Should().NotBe(row.Before);
        row.Before.Should().Contain("Original title");
        row.After.Should().Contain("Revised title");
    }

    // ── Delete (soft) ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteGoal_WritesAuditRow_ISSUE097()
    {
        Seed();
        var created = await Service().CreateAsync(Input());
        created.IsSuccess.Should().BeTrue();
        var goalId = created.Value!.Id;

        var deleted = await Service().DeleteAsync(goalId);
        deleted.IsSuccess.Should().BeTrue();

        var audits = await AuditsFor(goalId);

        audits.Should().Contain(a => ActionContains(a, "Deleted"),
            "goal deletion must write a Goal.Deleted audit row (ISSUE-097)");
        var row = audits.Single(a => ActionContains(a, "Deleted"));
        ActionContains(row, "Goal").Should().BeTrue();
        row.ResourceId.Should().Be(goalId.ToString());
        row.TenantId.Should().Be(_tenantId);
        row.UserId.Should().Be(_userId);
    }
}
