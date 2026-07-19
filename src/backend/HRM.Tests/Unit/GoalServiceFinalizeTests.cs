// ============================================================================
// BUG-056 (US-PRF-001) — finalize / lock the goal set after sign-off.
//
// GoalService.FinalizeGoalsAsync locks an employee's goal set for a cycle: it requires HR
// (SetGoal.All) or the employee's direct manager (SetGoal.Team) per BR-4, asserts the set's
// weights sum to EXACTLY 100% (else 422 weight_not_100), then transitions every goal to
// GoalStatus.Finalized. Thereafter Create/Update/Delete/SaveGoals reject writes to that set
// (409 goals_finalized) until it is re-opened (re-open flow out of scope).
//
// Drives the REAL GoalService through the InMemory-through-real-EF harness (goals/employees are
// plain rows). Mirrors GoalServiceSaveGoalsTests.
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

public sealed class GoalServiceFinalizeTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();

    public GoalServiceFinalizeTests()
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

        _notifications = Substitute.For<IPerformanceNotificationService>();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private GoalService Service() => new(
        Db(), _tenantContext, _currentUser, _notifications, Substitute.For<ILogger<GoalService>>());

    private GoalService Service(ICurrentUser currentUser) => new(
        Db(), _tenantContext, currentUser, _notifications, Substitute.For<ILogger<GoalService>>());

    private void Seed(bool windowOpen = true)
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
            GoalSettingStart = windowOpen ? now.AddDays(-5) : now.AddDays(-20),
            GoalSettingEnd = windowOpen ? now.AddDays(5) : now.AddDays(-10),
            SelfAssessmentStart = now.AddDays(10), SelfAssessmentEnd = now.AddDays(20),
            RatingScaleMax = 5, SelfWeightPercent = 30, IsDeleted = false,
        });
        db.SaveChanges();
    }

    private Guid SeedGoal(string title, int weight)
    {
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.Goals.Add(new Goal
        {
            Id = id, TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _employeeId,
            Title = title, Description = "seed", Category = GoalCategory.Kpi, Weight = weight,
            TargetValue = "100%", MeasurementUnit = "%",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = GoalStatus.Draft, IsDeleted = false,
        });
        db.SaveChanges();
        return id;
    }

    private static SaveGoalItem Item(Guid? id, string title, int weight)
        => new(id, title, "desc", GoalCategory.Kpi, weight, "100%", "%",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null);

    // ── Finalize at exactly 100% succeeds and flips every goal to Finalized ──

    [Fact]
    public async Task Finalize_AtExactly100_Succeeds_AndFlipsStatusToFinalized()
    {
        Seed();
        SeedGoal("A", 60);
        SeedGoal("B", 40);

        var result = await Service().FinalizeGoalsAsync(_employeeId, _cycleId);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.TotalWeight.Should().Be(100);
        result.Value.Goals.Should().OnlyContain(g => g.Status == GoalStatus.Finalized);

        using var db = Db();
        var live = await db.Goals.AsNoTracking()
            .Where(g => g.EmployeeId == _employeeId && g.CycleId == _cycleId)
            .ToListAsync();
        live.Should().HaveCount(2);
        live.Should().OnlyContain(g => g.Status == GoalStatus.Finalized);
    }

    // ── Finalize below 100% (95%) → 422 weight_not_100 ────────────────

    [Fact]
    public async Task Finalize_At95Percent_Returns422_WeightNot100()
    {
        Seed();
        SeedGoal("A", 60);
        SeedGoal("B", 35); // total 95

        var result = await Service().FinalizeGoalsAsync(_employeeId, _cycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("weight_not_100");

        // The set stays editable (nothing flipped to Finalized).
        using var db = Db();
        (await db.Goals.AsNoTracking().CountAsync(
            g => g.EmployeeId == _employeeId && g.Status == GoalStatus.Finalized)).Should().Be(0);
    }

    // ── A write to a finalized set → 409 goals_finalized ──────────────

    [Fact]
    public async Task Write_ToFinalizedSet_Returns409_GoalsFinalized()
    {
        Seed();
        SeedGoal("A", 60);
        SeedGoal("B", 40);

        var finalize = await Service().FinalizeGoalsAsync(_employeeId, _cycleId);
        finalize.IsSuccess.Should().BeTrue(finalize.Error);

        // A bulk save against the locked set is rejected.
        var save = await Service().SaveGoalsAsync(
            _employeeId, _cycleId, new[] { Item(null, "New goal", 100) });

        save.IsFailure.Should().BeTrue();
        save.StatusCode.Should().Be(409);
        save.ErrorCode.Should().Be("goals_finalized");

        // A single-goal create against the locked set is likewise rejected.
        var create = await Service().CreateAsync(new GoalInput(
            _cycleId, _employeeId, "Sneak in", null, GoalCategory.Kpi, 10, "1", "u",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null));

        create.IsFailure.Should().BeTrue();
        create.StatusCode.Should().Be(409);
        create.ErrorCode.Should().Be("goals_finalized");
    }

    // ── Re-finalizing an already-locked set → 409 goals_finalized ─────

    [Fact]
    public async Task Finalize_AlreadyFinalizedSet_Returns409_GoalsFinalized()
    {
        Seed();
        SeedGoal("A", 60);
        SeedGoal("B", 40);

        (await Service().FinalizeGoalsAsync(_employeeId, _cycleId)).IsSuccess.Should().BeTrue();

        var again = await Service().FinalizeGoalsAsync(_employeeId, _cycleId);

        again.IsFailure.Should().BeTrue();
        again.StatusCode.Should().Be(409);
        again.ErrorCode.Should().Be("goals_finalized");
    }

    // ── A non-manager finalizing → denied (403) ───────────────────────

    [Fact]
    public async Task Finalize_NonManager_Returns403()
    {
        Seed();
        SeedGoal("A", 60);
        SeedGoal("B", 40);

        // A caller with no SetGoal permissions at all.
        var caller = Substitute.For<ICurrentUser>();
        caller.UserId.Returns(Guid.NewGuid());
        caller.IsAuthenticated.Returns(true);
        caller.Email.Returns("nobody@acme.com");
        caller.Permissions.Returns(Array.Empty<string>());

        var result = await Service(caller).FinalizeGoalsAsync(_employeeId, _cycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("forbidden");

        // Nothing was locked.
        using var db = Db();
        (await db.Goals.AsNoTracking().CountAsync(
            g => g.EmployeeId == _employeeId && g.Status == GoalStatus.Finalized)).Should().Be(0);
    }

    // ── BUG-056: finalizing an empty set (0% total) → 422 weight_not_100 (boundary) ──
    [Fact]
    public async Task Finalize_EmptySet_Returns422_WeightNot100()
    {
        Seed();
        // No goals seeded ⇒ total weight 0 ≠ 100.
        var result = await Service().FinalizeGoalsAsync(_employeeId, _cycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("weight_not_100");
    }

    // ── BUG-056: Update and Delete against a finalized set are also blocked (the other two write-guards) ──
    [Fact]
    public async Task UpdateAndDelete_ToFinalizedSet_Returns409_GoalsFinalized()
    {
        Seed();
        var goalId = SeedGoal("A", 60);
        SeedGoal("B", 40);

        (await Service().FinalizeGoalsAsync(_employeeId, _cycleId)).IsSuccess.Should().BeTrue();

        var update = await Service().UpdateAsync(goalId, new GoalInput(
            _cycleId, _employeeId, "Edit", null, GoalCategory.Kpi, 60, "1", "u",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null));
        update.IsFailure.Should().BeTrue();
        update.StatusCode.Should().Be(409);
        update.ErrorCode.Should().Be("goals_finalized");

        var delete = await Service().DeleteAsync(goalId);
        delete.IsFailure.Should().BeTrue();
        delete.StatusCode.Should().Be(409);
        delete.ErrorCode.Should().Be("goals_finalized");
    }

    // ── BUG-056: a finalized set surfaces as "Finalized" on the manager team dashboard (not "Draft") ──
    [Fact]
    public async Task TeamDashboard_FinalizedSet_AggregatesAsFinalized()
    {
        Seed();
        SeedGoal("A", 60);
        SeedGoal("B", 40);

        // Link the caller to a manager employee and make the goal's employee report to that manager, so
        // GetTeamDashboardAsync (the manager's own team) includes the now-finalized report.
        var managerId = Guid.NewGuid();
        using (var db = Db())
        {
            db.Employees.Add(new Employee
            {
                Id = managerId, TenantId = _tenantId, UserId = _userId,
                EmployeeNo = "MGR-0001", FirstName = "Grace", LastName = "Hopper",
                Email = "grace@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
            });
            var report = await db.Employees.FirstAsync(e => e.Id == _employeeId);
            report.ReportsToEmployeeId = managerId;
            await db.SaveChangesAsync();
        }

        (await Service().FinalizeGoalsAsync(_employeeId, _cycleId)).IsSuccess.Should().BeTrue();

        var dashboard = await Service().GetTeamDashboardAsync(_cycleId);

        dashboard.IsSuccess.Should().BeTrue(dashboard.Error);
        dashboard.Value!.Members.Single(m => m.EmployeeId == _employeeId)
            .Status.Should().Be("Finalized");
    }
}
