// ============================================================================
// DF-47 — US-PRF-001 / BUG-056 / DF-46: the goal finalize + re-open state machine on REAL Postgres.
//
// GoalService persists GoalStatus as a STRING column (goal.status, varchar(20) via HasConversion<string>()),
// NOT an int. The InMemory unit suites (GoalServiceFinalizeTests / GoalServiceReopenTests) exercise the
// state machine, but InMemory ignores the value converter and the varchar length, so they can NOT prove:
//   1. the enum actually round-trips as the literal string "Finalized" on Npgsql (a broken converter would
//      store "3" or throw), and
//   2. the whole finalize -> lock (409) -> re-open -> writable flow + its audit_log writes COMMIT on Postgres.
// This suite closes that gap. The finalize/reopen guards + audit are the REAL GoalService driven directly
// (service-layer, like the other *PostgresTests) against a Testcontainers Postgres 17.
//
// Harness copied from AttendanceSettingsCrudPostgresTests. UseSnakeCaseNamingConvention() is NOT optional —
// omitting it makes MigrateAsync throw PendingModelChangesWarning. Each test uses fresh tenant/employee
// GUIDs so the shared (once-migrated) database cannot cross-contaminate between methods.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class GoalFinalizeReopenPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid(), Hr(Guid.NewGuid()));
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    /// <summary>An HR caller (Performance.SetGoal.All ⇒ authorizes for any in-tenant employee, BR-4).</summary>
    private static ICurrentUser Hr(Guid userId)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(userId);
        cu.Email.Returns("hr@acme.test");
        cu.Permissions.Returns(new[] { PermissionCatalog.Performance.SetGoalAll });
        return cu;
    }

    private AppDbContext Db(Guid tenantId, ICurrentUser currentUser)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(currentUser))
                .Options,
            tc);
    }

    private GoalService Service(AppDbContext db, Guid tenantId, ICurrentUser cu)
        => new(db, new FixedTenantContext { TenantId = tenantId }, cu,
            Substitute.For<IPerformanceNotificationService>(), NullLogger<GoalService>.Instance);

    // ── seeding ────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the minimum valid graph for goal writes: a Department + JobTitle (Employee has REQUIRED FK
    /// constraints on Postgres — InMemory ignores them, which is why the unit seeds omit them), the target
    /// Employee, and an AppraisalCycle whose goal-setting window is OPEN. Every DateTime is UTC-kind so the
    /// timestamptz columns accept it (the BUG-289/290 class InMemory hides).
    /// </summary>
    private async Task SeedGraphAsync(Guid tenantId, Guid employeeId, Guid cycleId, ICurrentUser cu)
    {
        await using var db = Db(tenantId, cu);
        var now = DateTime.UtcNow;

        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Eng", Code = "ENG" };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = "Engineer" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        db.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = "EMP-0001",
            FirstName = "Ada", LastName = "Lovelace", Email = "ada@acme.test",
            DepartmentId = dept.Id, JobTitleId = title.Id,
            Status = EmployeeStatus.Active, IsActive = true, IsDeleted = false,
        });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = cycleId, TenantId = tenantId, Name = "FY2026",
            Type = CycleType.Annual, Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-30), EndDate = now.AddDays(60),
            GoalSettingStart = now.AddDays(-5), GoalSettingEnd = now.AddDays(5),
            SelfAssessmentStart = now.AddDays(10), SelfAssessmentEnd = now.AddDays(20),
            ManagerReviewStart = now.AddDays(21), ManagerReviewEnd = now.AddDays(30),
            ParticipantScope = ParticipantScopeType.AllEmployees,
            RatingScaleMax = 5, SelfWeightPercent = 30, IsDeleted = false,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a Draft goal directly (bypasses the service) so a set can be assembled to finalize.</summary>
    private async Task SeedGoalAsync(Guid tenantId, Guid employeeId, Guid cycleId, string title, int weight, ICurrentUser cu)
    {
        await using var db = Db(tenantId, cu);
        db.Goals.Add(new Goal
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, CycleId = cycleId, EmployeeId = employeeId,
            Title = title, Description = "seed", Category = GoalCategory.Kpi, Weight = weight,
            TargetValue = "100%", MeasurementUnit = "%",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = GoalStatus.Draft, IsDeleted = false,
        });
        await db.SaveChangesAsync();
    }

    private static SaveGoalItem Item(string title, int weight)
        => new(null, title, "desc", GoalCategory.Kpi, weight, "100%", "%",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null);

    // ══ Arm 1 — the GoalStatus enum persists as the string "Finalized", not an int ══

    /// <summary>
    /// Finalize a 100% set via the REAL service, then read the RAW goal.status column with SQL (bypassing the
    /// value converter). It must be the literal text "Finalized" — a broken/absent string converter would
    /// store "3" (the enum's int) or fail the insert. Also read it back through EF to prove the converter maps
    /// the stored string back to GoalStatus.Finalized. This is the Npgsql string-enum round-trip InMemory
    /// cannot exercise.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PRF-001-14")]
    public async Task Finalize_PersistsStatusAsEnumString_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        var cu = Hr(Guid.NewGuid());

        await SeedGraphAsync(tenantId, employeeId, cycleId, cu);
        await SeedGoalAsync(tenantId, employeeId, cycleId, "A", 60, cu);
        await SeedGoalAsync(tenantId, employeeId, cycleId, "B", 40, cu);

        await using (var db = Db(tenantId, cu))
        {
            var finalize = await Service(db, tenantId, cu).FinalizeGoalsAsync(employeeId, cycleId);
            finalize.IsSuccess.Should().BeTrue(finalize.Error);
        }

        await using (var verify = Db(tenantId, cu))
        {
            var goalId = await verify.Goals.AsNoTracking()
                .Where(g => g.EmployeeId == employeeId && g.CycleId == cycleId)
                .Select(g => g.Id).FirstAsync();

            // RAW read of the persisted column value — proves the STRING, not the int, is on disk.
            // Scalar SqlQuery requires the column aliased to "Value". TODO(orchestrator-verify): confirm the
            // EF-Core scalar-projection alias convention still holds on this EF/Npgsql version.
            var raw = await verify.Database
                .SqlQueryRaw<string>("SELECT status AS \"Value\" FROM goal WHERE id = {0}", goalId)
                .SingleAsync();
            raw.Should().Be("Finalized", "GoalStatus must persist as its enum name, not the int 3");

            // And the converter maps the stored string back to the enum on read.
            var reread = await verify.Goals.AsNoTracking().SingleAsync(g => g.Id == goalId);
            reread.Status.Should().Be(GoalStatus.Finalized);
        }
    }

    // ══ Arm 2 — finalize -> lock (409) -> re-open (Acknowledged) -> writable, end-to-end on Postgres ══

    /// <summary>
    /// The full BUG-056/DF-46 state machine committed on real Postgres: finalize flips every goal to
    /// Finalized; a SaveGoals against the locked set returns 409 goals_finalized; re-open with a reason flips
    /// them to Acknowledged; the SAME SaveGoals now SUCCEEDS. The before/after 409→success contrast is the
    /// real proof the lock is genuine and genuinely lifted (not a status flip nobody reads). Also asserts the
    /// Goal.Reopened audit_log row carries the reason in Detail — proving the audit write commits on Postgres.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PRF-001-15")]
    public async Task Finalize_Lock_Reopen_RestoresWritability_AndWritesReopenAudit_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        var cu = Hr(Guid.NewGuid());

        await SeedGraphAsync(tenantId, employeeId, cycleId, cu);
        await SeedGoalAsync(tenantId, employeeId, cycleId, "A", 60, cu);
        await SeedGoalAsync(tenantId, employeeId, cycleId, "B", 40, cu);

        await using (var db = Db(tenantId, cu))
            (await Service(db, tenantId, cu).FinalizeGoalsAsync(employeeId, cycleId))
                .IsSuccess.Should().BeTrue();

        // Locked: a bulk save is rejected 409 goals_finalized.
        await using (var db = Db(tenantId, cu))
        {
            var blocked = await Service(db, tenantId, cu)
                .SaveGoalsAsync(employeeId, cycleId, new[] { Item("Blocked", 100) });
            blocked.IsFailure.Should().BeTrue();
            blocked.StatusCode.Should().Be(409);
            blocked.ErrorCode.Should().Be("goals_finalized");
        }

        // Re-open with a reason → every Finalized goal returns to Acknowledged.
        await using (var db = Db(tenantId, cu))
        {
            var reopen = await Service(db, tenantId, cu)
                .ReopenGoalsAsync(employeeId, cycleId, "  Wrong cycle finalized by mistake.  ");
            reopen.IsSuccess.Should().BeTrue(reopen.Error);
            reopen.Value!.Goals.Should().OnlyContain(g => g.Status == GoalStatus.Acknowledged);
        }

        await using (var verify = Db(tenantId, cu))
        {
            var live = await verify.Goals.AsNoTracking()
                .Where(g => g.EmployeeId == employeeId && g.CycleId == cycleId).ToListAsync();
            live.Should().HaveCount(2);
            live.Should().OnlyContain(g => g.Status == GoalStatus.Acknowledged);
            live.Should().NotContain(g => g.Status == GoalStatus.Finalized);

            // The re-open audit row exists per goal, with the TRIMMED reason in Detail (the whole point of
            // making the reason mandatory — it must land in the trail).
            var reopenAudits = await verify.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "Goal.Reopened" && a.ResourceType == "Goal").ToListAsync();
            reopenAudits.Should().HaveCount(2);
            reopenAudits.Should().OnlyContain(a =>
                a.Detail != null && a.Detail.Contains("Reason: Wrong cycle finalized by mistake."));
        }

        // Writable again: the SAME SaveGoals that was 409 while locked now succeeds and commits.
        await using (var db = Db(tenantId, cu))
        {
            var save = await Service(db, tenantId, cu)
                .SaveGoalsAsync(employeeId, cycleId, new[] { Item("New goal", 50), Item("Another", 50) });
            save.IsSuccess.Should().BeTrue(save.Error);
        }

        await using (var verify = Db(tenantId, cu))
        {
            var live = await verify.Goals.AsNoTracking()
                .Where(g => g.EmployeeId == employeeId && g.CycleId == cycleId && !g.IsDeleted).ToListAsync();
            live.Should().HaveCount(2, "the full-replace SaveGoals replaced the re-opened set");
            live.Sum(g => g.Weight).Should().Be(100);
        }
    }

    // ══ Arm 3 — cross-tenant: tenant B cannot finalize or re-open tenant A's set (global filter translates) ══

    /// <summary>
    /// BUG-003 class on real Postgres. Tenant B (its own HR) cannot finalize or re-open tenant A's goal set:
    /// the EF global query filter makes A's Employee invisible to B, so authorization resolves to
    /// employee_not_found (404) and the mutation can never reach A's goals. Proven by asserting A's set is
    /// untouched from a tenant-A context after each B attempt.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PRF-001-15")]
    public async Task CrossTenant_CannotFinalizeOrReopen_AnotherTenantsSet()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var cycleA = Guid.NewGuid();
        var hrA = Hr(Guid.NewGuid());
        var hrB = Hr(Guid.NewGuid());

        await SeedGraphAsync(tenantA, employeeA, cycleA, hrA);
        await SeedGoalAsync(tenantA, employeeA, cycleA, "A", 60, hrA);
        await SeedGoalAsync(tenantA, employeeA, cycleA, "B", 40, hrA);

        // (1) Tenant B tries to FINALIZE tenant A's (still-Draft) set → invisible employee → 404, nothing locked.
        await using (var db = Db(tenantB, hrB))
        {
            var finalizeByB = await Service(db, tenantB, hrB).FinalizeGoalsAsync(employeeA, cycleA);
            finalizeByB.IsFailure.Should().BeTrue("B must not finalize A's set");
            finalizeByB.StatusCode.Should().Be(404);
            finalizeByB.ErrorCode.Should().Be("employee_not_found");
        }

        await using (var verify = Db(tenantA, hrA))
            (await verify.Goals.AsNoTracking().CountAsync(
                g => g.EmployeeId == employeeA && g.Status == GoalStatus.Finalized))
                .Should().Be(0, "B's finalize attempt must not have touched A's goals");

        // Tenant A legitimately finalizes its own set.
        await using (var db = Db(tenantA, hrA))
            (await Service(db, tenantA, hrA).FinalizeGoalsAsync(employeeA, cycleA)).IsSuccess.Should().BeTrue();

        // (2) Tenant B tries to RE-OPEN tenant A's now-finalized set → 404, set stays locked.
        await using (var db = Db(tenantB, hrB))
        {
            var reopenByB = await Service(db, tenantB, hrB)
                .ReopenGoalsAsync(employeeA, cycleA, "Cross-tenant unlock attempt.");
            reopenByB.IsFailure.Should().BeTrue("B must not re-open A's set");
            reopenByB.StatusCode.Should().Be(404);
            reopenByB.ErrorCode.Should().Be("employee_not_found");
        }

        await using (var verify = Db(tenantA, hrA))
            (await verify.Goals.AsNoTracking().CountAsync(
                g => g.EmployeeId == employeeA && g.Status == GoalStatus.Finalized))
                .Should().Be(2, "A's set must still be finalized — B's re-open could not reach it");
    }
}
