// ============================================================================
// US-PRF-009: Goal tracking with progress updates — integration tests.
//
// Exercises GoalProgressService + StaleGoalNudgeService over a real AppDbContext (InMemory) with the
// ITenantContext-driven global query filter, covering the cross-cutting concerns a service unit test cannot:
//   - NFR-2 tenant isolation: a goal + its progress in Tenant A are invisible from Tenant B (cross-tenant My Goals
//     / timeline / team summary return nothing for the other tenant).
//   - NFR-3 IMMUTABILITY: across fresh scoped contexts the recorded progress-update rows keep their original values;
//     the service has no update/delete path for them.
//   - AC-5/FR-6/BR-4 stale-goal sweep: an active goal with no update past the configurable window nudges the
//     employee; a fresh goal does not; setting the interval to 0 disables the sweep; the sweep never crosses tenants.
//
// PROVIDER: InMemory — same rationale as the other Performance integration tests (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker).
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class GoalProgressIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private GoalProgressService Service(Guid tenantId, Guid userId, params string[] permissions)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("u@t.com");
        user.Permissions.Returns(permissions);

        return new GoalProgressService(db, ctx, user, Substitute.For<IPerformanceNotificationService>(),
            NullLogger<GoalProgressService>.Instance);
    }

    private StaleGoalNudgeService Sweep(Guid tenantId, IPerformanceNotificationService notifications)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);
        return new StaleGoalNudgeService(db, ctx, notifications, NullLogger<StaleGoalNudgeService>.Instance);
    }

    private async Task<(Guid empUserId, Guid empId, Guid mgrUserId, Guid mgrId, Guid cycleId, Guid goalId)> SeedTenantAsync(
        Guid tenantId, int nudgeDays = 14, DateTime? goalSettingEnd = null)
    {
        var empUserId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var mgrUserId = Guid.NewGuid();
        var mgrId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        var goalId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using var db = Db(tenantId);
        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "t" + tenantId.ToString("N")[..4], Name = "T", Status = TenantStatus.Active, StaleGoalNudgeDays = nudgeDays });
        db.Employees.Add(new Employee { Id = mgrId, TenantId = tenantId, UserId = mgrUserId, EmployeeNo = "MGR", FirstName = "M", LastName = "Gr", Email = "m@t.com", Status = EmployeeStatus.Active });
        db.Employees.Add(new Employee { Id = empId, TenantId = tenantId, UserId = empUserId, EmployeeNo = "EMP", FirstName = "E", LastName = "Mp", Email = "e@t.com", Status = EmployeeStatus.Active, ReportsToEmployeeId = mgrId });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = cycleId, TenantId = tenantId, Name = "C", Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-30), EndDate = now.AddDays(60),
            GoalSettingStart = now.AddDays(-30), GoalSettingEnd = goalSettingEnd ?? now.AddDays(-7),
            SelfAssessmentStart = now.AddDays(40), SelfAssessmentEnd = now.AddDays(50),
        });
        db.Goals.Add(new Goal { Id = goalId, TenantId = tenantId, CycleId = cycleId, EmployeeId = empId, Title = "Goal", Weight = 100, Status = GoalStatus.Acknowledged, TargetValue = "x", MeasurementUnit = "u", DueDate = DateOnly.FromDateTime(now.AddDays(30)) });
        await db.SaveChangesAsync();

        return (empUserId, empId, mgrUserId, mgrId, cycleId, goalId);
    }

    // ── NFR-2: tenant isolation ─────────────────────────────────────────

    [Fact]
    public async Task Progress_in_tenant_A_is_invisible_from_tenant_B()
    {
        var a = await SeedTenantAsync(_tenantA);
        var b = await SeedTenantAsync(_tenantB);

        await Service(_tenantA, a.empUserId, PermissionCatalog.Performance.ReadSelf)
            .AddProgressUpdateAsync(new AddGoalProgressInput(a.goalId, 55, GoalProgressStatus.InProgress, "A note", []));

        // Tenant B HR can never see tenant A's goal/timeline.
        var crossTimeline = await Service(_tenantB, b.mgrUserId, PermissionCatalog.Performance.ReviewAll)
            .GetGoalTimelineAsync(a.goalId);
        crossTimeline.IsFailure.Should().BeTrue();
        crossTimeline.StatusCode.Should().Be(404);

        // Tenant B HR's team summary never includes tenant A's employee.
        var teamB = await Service(_tenantB, b.mgrUserId, PermissionCatalog.Performance.ReviewAll).GetTeamSummaryAsync();
        teamB.Value!.Should().NotContain(r => r.EmployeeId == a.empId);
    }

    // ── NFR-3: immutability across fresh contexts ───────────────────────

    [Fact]
    public async Task Recorded_update_is_immutable_across_fresh_contexts()
    {
        var a = await SeedTenantAsync(_tenantA);
        var result = await Service(_tenantA, a.empUserId, PermissionCatalog.Performance.ReadSelf)
            .AddProgressUpdateAsync(new AddGoalProgressInput(a.goalId, 42, GoalProgressStatus.InProgress, "evidence", []));
        var updateId = result.Value!.Updates.Single().Id;

        // Re-read from a brand new context: the row is byte-for-byte the same; there is no mutate path.
        using var db = Db(_tenantA);
        var persisted = await db.GoalProgressUpdates.AsNoTracking().SingleAsync(u => u.Id == updateId);
        persisted.ProgressPct.Should().Be(42);
        persisted.Notes.Should().Be("evidence");
        persisted.Status.Should().Be(GoalProgressStatus.InProgress);
    }

    // ── AC-5/FR-6/BR-4: stale-goal sweep ────────────────────────────────

    [Fact]
    public async Task Sweep_nudges_a_stale_goal_with_no_update()
    {
        // goal-setting closed 20 days ago, no update, interval 14 ⇒ stale.
        var a = await SeedTenantAsync(_tenantA, nudgeDays: 14, goalSettingEnd: DateTime.UtcNow.AddDays(-20));
        var notifications = Substitute.For<IPerformanceNotificationService>();

        var dispatched = await Sweep(_tenantA, notifications).RunSweepAsync(DateTime.UtcNow);

        dispatched.Should().Be(1);
        await notifications.Received().NotifyGoalProgressAsync(
            "goal-stale-nudge", a.goalId, a.empId, a.empId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_skips_a_recently_updated_goal()
    {
        var a = await SeedTenantAsync(_tenantA, nudgeDays: 14, goalSettingEnd: DateTime.UtcNow.AddDays(-20));
        await Service(_tenantA, a.empUserId, PermissionCatalog.Performance.ReadSelf)
            .AddProgressUpdateAsync(new AddGoalProgressInput(a.goalId, 30, GoalProgressStatus.InProgress, "fresh", []));
        var notifications = Substitute.For<IPerformanceNotificationService>();

        var dispatched = await Sweep(_tenantA, notifications).RunSweepAsync(DateTime.UtcNow);

        dispatched.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_is_disabled_when_nudge_days_is_zero()
    {
        var a = await SeedTenantAsync(_tenantA, nudgeDays: 0, goalSettingEnd: DateTime.UtcNow.AddDays(-100));
        var notifications = Substitute.For<IPerformanceNotificationService>();

        var dispatched = await Sweep(_tenantA, notifications).RunSweepAsync(DateTime.UtcNow);

        dispatched.Should().Be(0);
        await notifications.DidNotReceive().NotifyGoalProgressAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_never_crosses_tenants()
    {
        // Tenant A stale; Tenant B fresh. Running the sweep for A only nudges A's goal.
        var a = await SeedTenantAsync(_tenantA, nudgeDays: 14, goalSettingEnd: DateTime.UtcNow.AddDays(-20));
        var b = await SeedTenantAsync(_tenantB, nudgeDays: 14, goalSettingEnd: DateTime.UtcNow.AddDays(-20));
        var notifications = Substitute.For<IPerformanceNotificationService>();

        var dispatched = await Sweep(_tenantA, notifications).RunSweepAsync(DateTime.UtcNow);

        dispatched.Should().Be(1);
        await notifications.Received().NotifyGoalProgressAsync(
            "goal-stale-nudge", a.goalId, a.empId, a.empId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await notifications.DidNotReceive().NotifyGoalProgressAsync(
            "goal-stale-nudge", b.goalId, b.empId, b.empId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_is_idempotent_same_day_no_double_nudge_issue142()
    {
        // ISSUE-142 (TC-009-04): a same-day re-run / retry must NOT re-dispatch the nudge.
        var a = await SeedTenantAsync(_tenantA, nudgeDays: 14, goalSettingEnd: DateTime.UtcNow.AddDays(-20));
        var notifications = Substitute.For<IPerformanceNotificationService>();
        var now = DateTime.UtcNow;

        var first = await Sweep(_tenantA, notifications).RunSweepAsync(now);
        var second = await Sweep(_tenantA, notifications).RunSweepAsync(now);

        first.Should().Be(1);
        second.Should().Be(0); // already nudged today → skipped
        await notifications.Received(1).NotifyGoalProgressAsync(
            "goal-stale-nudge", a.goalId, a.empId, a.empId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_re_nudges_on_a_later_day_issue142()
    {
        // The dedup is per-DAY, not permanent — a genuinely later run nudges again.
        var a = await SeedTenantAsync(_tenantA, nudgeDays: 14, goalSettingEnd: DateTime.UtcNow.AddDays(-20));
        var notifications = Substitute.For<IPerformanceNotificationService>();
        var day1 = DateTime.UtcNow;

        var first = await Sweep(_tenantA, notifications).RunSweepAsync(day1);
        var second = await Sweep(_tenantA, notifications).RunSweepAsync(day1.AddDays(1));

        first.Should().Be(1);
        second.Should().Be(1); // a new day → nudged again
    }
}
