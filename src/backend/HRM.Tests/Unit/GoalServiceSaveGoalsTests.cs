// ============================================================================
// BUG-243 (US-PRF-001) — bulk full-replace SaveGoals endpoint.
//
// GoalService.SaveGoalsAsync replaces an employee's whole goal set for a cycle in one
// atomic SaveChanges: items with a matching Id are updated, Id-less/unknown items are
// created as Draft, and existing goals absent from the incoming set are soft-deleted.
// It reuses the same rules as the per-goal Create/Update path: tenant-resolved guard,
// AuthorizeForEmployeeAsync (BR-4), the goal-setting-window gate (BR-1/AC-5), ≤10 count
// (BR-2), ≤100% total weight (FR-3), and parent-goal existence (FR-4). It emits per-change
// Goal.Created/Updated/Deleted audit rows (FR-6) and exactly ONE aggregate notification (FR-7).
//
// Drives the REAL GoalService through the InMemory-through-real-EF harness (goals are plain
// rows — no jsonb/RLS to mask, so InMemory is faithful here). Mirrors GoalServiceAuditTests.
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

public sealed class GoalServiceSaveGoalsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();

    public GoalServiceSaveGoalsTests()
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

    // ── Seeding ──────────────────────────────────────────────────────

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

    private static SaveGoalItem Item(Guid? id, string title, int weight, Guid? parentGoalId = null)
        => new(id, title, "desc", GoalCategory.Kpi, weight, "100%", "%",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), parentGoalId);

    private async Task<List<AuditLog>> AuditsForResource(Guid goalId)
    {
        using var db = Db();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.ResourceId == goalId.ToString())
            .ToListAsync();
    }

    private static bool ActionContains(AuditLog a, string s)
        => (a.Action?.Contains(s) ?? false) || (a.EventType?.Contains(s) ?? false);

    // ── Full-replace: create + update + delete in one call ────────────

    [Fact]
    public async Task SaveGoals_FullReplace_CreatesUpdatesAndSoftDeletes_InOneCall()
    {
        Seed();
        var keepId = SeedGoal("Keep me", 30);   // will be updated
        var dropId = SeedGoal("Drop me", 20);   // absent from incoming set → soft-deleted

        var request = new[]
        {
            Item(keepId, "Kept and revised", 40),  // update
            Item(null, "Brand new goal", 25),    // create
        };

        var result = await Service().SaveGoalsAsync(_employeeId, _cycleId, request);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Goals.Should().HaveCount(2);
        result.Value.TotalWeight.Should().Be(65);
        result.Value.Goals.Should().Contain(g => g.Title == "Kept and revised" && g.Weight == 40);
        result.Value.Goals.Should().Contain(g => g.Title == "Brand new goal" && g.Weight == 25);

        // Live rows: keep updated, new created (both non-deleted); dropped one soft-deleted.
        using var db = Db();
        var live = await db.Goals.AsNoTracking()
            .Where(g => g.EmployeeId == _employeeId && g.CycleId == _cycleId)
            .ToListAsync();
        live.Should().HaveCount(2);
        live.Should().OnlyContain(g => !g.IsDeleted);

        var dropped = await db.Goals.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(g => g.Id == dropId);
        dropped.IsDeleted.Should().BeTrue("goals absent from the incoming set are soft-deleted");
    }

    // ── Audit rows (FR-6) ─────────────────────────────────────────────

    [Fact]
    public async Task SaveGoals_EmitsCreatedUpdatedDeletedAuditRows_FR6()
    {
        Seed();
        var keepId = SeedGoal("Keep me", 30);
        var dropId = SeedGoal("Drop me", 20);

        var request = new[]
        {
            Item(keepId, "Kept and revised", 40),
            Item(null, "Brand new goal", 25),
        };

        var result = await Service().SaveGoalsAsync(_employeeId, _cycleId, request);
        result.IsSuccess.Should().BeTrue(result.Error);

        var newId = result.Value!.Goals.Single(g => g.Title == "Brand new goal").Id;

        (await AuditsForResource(keepId)).Should().Contain(a => ActionContains(a, "Updated"),
            "an in-place update must write a Goal.Updated audit row");
        (await AuditsForResource(newId)).Should().Contain(a => ActionContains(a, "Created"),
            "a created goal must write a Goal.Created audit row");
        (await AuditsForResource(dropId)).Should().Contain(a => ActionContains(a, "Deleted"),
            "a soft-deleted goal must write a Goal.Deleted audit row");

        // The update audit captures before/after.
        var updateRow = (await AuditsForResource(keepId)).Single(a => ActionContains(a, "Updated"));
        updateRow.Before.Should().Contain("Keep me");
        updateRow.After.Should().Contain("Kept and revised");
        updateRow.TenantId.Should().Be(_tenantId);
        updateRow.UserId.Should().Be(_userId);
    }

    // ── Notification (FR-7): exactly one aggregate, not one per goal ──

    [Fact]
    public async Task SaveGoals_SendsExactlyOneAggregateNotification_FR7()
    {
        Seed();
        SeedGoal("Existing A", 20);
        SeedGoal("Existing B", 20);

        var request = new[]
        {
            Item(null, "New 1", 20),
            Item(null, "New 2", 20),
            Item(null, "New 3", 20),
        };

        var result = await Service().SaveGoalsAsync(_employeeId, _cycleId, request);
        result.IsSuccess.Should().BeTrue(result.Error);

        await _notifications.Received(1).NotifyGoalChangedAsync(
            "goal-assigned", Arg.Any<Guid>(), _employeeId, _cycleId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveGoals_EmptySet_SoftDeletesAll_AndSendsNoNotification()
    {
        Seed();
        var aId = SeedGoal("A", 40);
        var bId = SeedGoal("B", 40);

        var result = await Service().SaveGoalsAsync(_employeeId, _cycleId, []);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Goals.Should().BeEmpty();

        using var db = Db();
        (await db.Goals.AsNoTracking().CountAsync(g => g.EmployeeId == _employeeId)).Should().Be(0);
        var a = await db.Goals.IgnoreQueryFilters().AsNoTracking().FirstAsync(g => g.Id == aId);
        var b = await db.Goals.IgnoreQueryFilters().AsNoTracking().FirstAsync(g => g.Id == bId);
        a.IsDeleted.Should().BeTrue();
        b.IsDeleted.Should().BeTrue();

        await _notifications.DidNotReceive().NotifyGoalChangedAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── BR-2: count > 10 → 409 ────────────────────────────────────────

    [Fact]
    public async Task SaveGoals_CountOverTen_Returns409()
    {
        Seed();

        var request = Enumerable.Range(0, 11)
            .Select(i => Item(null, $"Goal {i}", 5))
            .ToArray();

        var result = await Service().SaveGoalsAsync(_employeeId, _cycleId, request);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("goal_limit_reached");
    }

    // ── FR-3: total weight > 100 → 422 ────────────────────────────────

    [Fact]
    public async Task SaveGoals_TotalWeightOverHundred_Returns422()
    {
        Seed();

        var request = new[]
        {
            Item(null, "A", 40),
            Item(null, "B", 40),
            Item(null, "C", 40), // total 120
        };

        var result = await Service().SaveGoalsAsync(_employeeId, _cycleId, request);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("weight_exceeds_100");
    }

    // ── BR-1/AC-5: goal-setting window closed → 409 ───────────────────

    [Fact]
    public async Task SaveGoals_ClosedWindow_Returns409()
    {
        Seed(windowOpen: false);

        var request = new[] { Item(null, "A", 40) };

        var result = await Service().SaveGoalsAsync(_employeeId, _cycleId, request);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("goal_setting_closed");
    }

    // ── BR-4: not the direct manager (Team scope only) → 403 ──────────

    [Fact]
    public async Task SaveGoals_NotDirectReport_Returns403()
    {
        Seed();

        // A manager who holds only SetGoal.Team and is NOT the target employee's manager.
        var managerUserId = Guid.NewGuid();
        var managerEmpId = Guid.NewGuid();
        using (var db = Db())
        {
            db.Employees.Add(new Employee
            {
                Id = managerEmpId, TenantId = _tenantId, UserId = managerUserId,
                EmployeeNo = "EMP-9000", FirstName = "Not", LastName = "Manager",
                Email = "notmgr@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
            });
            // Target reports to somebody else entirely.
            var target = await db.Employees.FirstAsync(e => e.Id == _employeeId);
            target.ReportsToEmployeeId = Guid.NewGuid();
            db.SaveChanges();
        }

        var managerUser = Substitute.For<ICurrentUser>();
        managerUser.UserId.Returns(managerUserId);
        managerUser.IsAuthenticated.Returns(true);
        managerUser.Email.Returns("notmgr@acme.com");
        managerUser.Permissions.Returns(new[] { PermissionCatalog.Performance.SetGoalTeam });

        var request = new[] { Item(null, "A", 40) };

        var result = await Service(managerUser).SaveGoalsAsync(_employeeId, _cycleId, request);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_direct_report");
    }
}
