// ============================================================================
// US-RPT-005: Dashboard with KPI Widgets — integration tests.
//
// Exercises DashboardService over a real AppDbContext (InMemory) with the
// ITenantContext-driven global query filter and a server-side-resolved role,
// composing the real HrReportService plus NSubstitute stubs for the per-module
// collaborators. Covers:
//   - Role selection: HR vs manager vs employee return the right widget set.
//   - HR widget VALUES: 50 employees / 5 pending leave / 3 open positions.
//   - Manager team scoping: 8 direct reports → team-size = 8.
//   - Trend calc (BR-2): 2 months of headcount joiners → correct direction + %.
//   - AC-5 tenant isolation: Tenant A vs Tenant B dashboards are independent.
//   - BR-3: pending-approvals counts only items assigned to the logged-in user
//           (the manager-scoped queue), not the tenant-wide pending total.
//
// PROVIDER: InMemory — same rationale as the other integration tests (the verify
// gate runs `dotnet test` with no PostgreSQL / Docker). The service is written to
// be InMemory-safe. No IDistributedCache is wired → each call computes fresh.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Dashboard.DTOs;
using HRM.Application.Features.Holidays.DTOs;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit; // FakeTimeProvider (hand-rolled TimeProvider, reused across the HRM.Tests assembly)
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class DashboardIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    // ── Test doubles ─────────────────────────────────────────────────────────

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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; set; }
        public string Email => "u@t.com";
        public Guid TenantId { get; set; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles { get; set; } = [];
        public IReadOnlyList<string> Permissions { get; set; } = [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            // ISSUE-285(a): wire the interceptor so seeded employees get Employee.BirthMonthDay populated on the
            // InMemory provider (mirrors production), which the upcoming-birthdays widget's index-backed query reads.
            .AddInterceptors(new EmployeeBirthMonthDayInterceptor())
            .Options;
        return new AppDbContext(options, ctx);
    }

    private (AppDbContext Db, DashboardService Svc, FakeCurrentUser User) Scope(
        Guid tenantId,
        Guid userId,
        IReadOnlyList<string> permissions,
        IReadOnlyList<string>? roles = null,
        IVacancyService? vacancies = null,
        ILeaveRequestService? leaveRequests = null,
        ILeaveDashboardService? leaveDashboard = null,
        IAttendanceDashboardService? attendance = null,
        IOnboardingChecklistService? onboarding = null,
        IHolidayService? holidays = null,
        IAppraisalCycleService? appraisalCycles = null,
        IMyPayslipService? payslips = null,
        TimeProvider? timeProvider = null)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .AddInterceptors(new EmployeeBirthMonthDayInterceptor())
            .Options;
        var db = new AppDbContext(options, ctx);
        var user = new FakeCurrentUser { UserId = userId, TenantId = tenantId, Permissions = permissions, Roles = roles ?? [] };
        var hrReports = new HrReportService(db, ctx, NullLogger<HrReportService>.Instance);
        var svc = new DashboardService(
            db, ctx, user, hrReports, NullLogger<DashboardService>.Instance,
            vacancies, leaveRequests, leaveDashboard, attendance, onboarding, holidays, appraisalCycles, payslips,
            cache: null, timeProvider: timeProvider);
        return (db, svc, user);
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    private async Task<Guid> SeedDepartment(Guid tenantId, string name)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department
        {
            Id = id, TenantId = tenantId, Name = name,
            Code = name[..Math.Min(3, name.Length)].ToUpperInvariant(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedEmployee(
        Guid tenantId, string no, Guid departmentId,
        EmployeeStatus status = EmployeeStatus.Active,
        DateTime? joining = null,
        Guid? userId = null,
        Guid? reportsTo = null,
        DateTime? dob = null)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = joining ?? new DateTime(2020, 1, 1),
            EmploymentType = EmploymentType.FullTime, Status = status,
            IsActive = status is EmployeeStatus.Active or EmployeeStatus.Probation,
            DepartmentId = departmentId, JobTitleId = BaseEntity.NewUuidV7(),
            UserId = userId, ReportsToEmployeeId = reportsTo, DateOfBirth = dob,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedPendingLeave(Guid tenantId, Guid employeeId)
    {
        using var db = Db(tenantId);
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            LeaveTypeId = BaseEntity.NewUuidV7(), Status = LeaveRequestStatus.Pending,
        });
        await db.SaveChangesAsync();
    }

    private static IReadOnlyList<string> HrPerms() => [PermissionCatalog.Employee.ViewAll, PermissionCatalog.Reports.View];

    // ════════════════════════════════════════════════════════════════════════
    //  Role selection — HR
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HrDashboard_ReturnsHrWidgetSet_WithCorrectHeadcountAndPendingLeave()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        // 50 employees (Test Hint).
        for (int i = 0; i < 50; i++)
            await SeedEmployee(_tenantA, $"E{i}", dept);
        // 5 pending leave requests.
        for (int i = 0; i < 5; i++)
            await SeedPendingLeave(_tenantA, BaseEntity.NewUuidV7());

        // 3 open positions via the vacancy service stub.
        var vacancies = Substitute.For<IVacancyService>();
        vacancies.ListAsync(VacancyStatus.Open, null, null, 1, 1, Arg.Any<CancellationToken>())
            .Returns(Result<PagedResult<VacancyListItemDto>>.Success(new PagedResult<VacancyListItemDto> { TotalCount = 3 }));

        // Today's attendance (HR "all" scope) via the attendance dashboard stub.
        var attendance = Substitute.For<IAttendanceDashboardService>();
        attendance.GetKpisAsync(Arg.Any<DateOnly>(), "all", Arg.Any<CancellationToken>())
            .Returns(Result<DashboardKpiDto>.Success(new DashboardKpiDto { AttendancePercent = 92.5m }));

        var (db, svc, _) = Scope(_tenantA, Guid.NewGuid(), HrPerms(), vacancies: vacancies, attendance: attendance);
        using (db)
        {
            var result = await svc.GetWidgetsAsync();

            result.IsSuccess.Should().BeTrue();
            var dash = result.Value!;
            dash.Role.Should().Be("hr");

            var keys = dash.Widgets.Select(w => w.WidgetKey).ToList();
            keys.Should().Contain(new[]
            {
                "headcount", "open-positions", "pending-leave", "attendance-today",
                "upcoming-birthdays", "recent-joiners", "onboarding-in-progress", "turnover-rate",
            });
            // Manager/employee widgets must NOT appear on the HR dashboard.
            keys.Should().NotContain("team-size");
            keys.Should().NotContain("leave-balance");

            Widget(dash, "headcount").Value.Should().Be(50m);
            Widget(dash, "pending-leave").Value.Should().Be(5);
            Widget(dash, "open-positions").Value.Should().Be(3);
            Widget(dash, "attendance-today").Value.Should().Be(92.5m);
            Widget(dash, "attendance-today").Unit.Should().Be("%");
            // AC-4 click-through.
            Widget(dash, "pending-leave").LinkUrl.Should().Be("/leave/requests");
            Widget(dash, "pending-leave").LinkFilters!["status"].Should().Be("Pending");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Role selection — Manager (team-scoped)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ManagerDashboard_TeamSize_CountsOnlyDirectReports()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        var managerUserId = Guid.NewGuid();
        var managerEmpId = await SeedEmployee(_tenantA, "MGR", dept, userId: managerUserId);
        // 8 direct reports (Test Hint).
        for (int i = 0; i < 8; i++)
            await SeedEmployee(_tenantA, $"R{i}", dept, reportsTo: managerEmpId);
        // A non-report employee that must NOT be counted in team-size.
        await SeedEmployee(_tenantA, "OTHER", dept);

        // Manager has NO HR perm → role resolves to "manager" via the direct-report check.
        var leaveRequests = Substitute.For<ILeaveRequestService>();
        leaveRequests.GetPendingForManagerAsync(Arg.Any<PendingLeaveQueueQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Result<PendingLeaveQueueResult>.Success(new PendingLeaveQueueResult { TotalCount = 2 }));
        var attendance = Substitute.For<IAttendanceDashboardService>();
        attendance.GetKpisAsync(Arg.Any<DateOnly>(), "team", Arg.Any<CancellationToken>())
            .Returns(Result<DashboardKpiDto>.Success(new DashboardKpiDto { AttendancePercent = 88m }));

        var (db, svc, _) = Scope(
            _tenantA, managerUserId, [PermissionCatalog.Employee.ViewTeam], leaveRequests: leaveRequests, attendance: attendance);
        using (db)
        {
            var result = await svc.GetWidgetsAsync();

            result.IsSuccess.Should().BeTrue();
            var dash = result.Value!;
            dash.Role.Should().Be("manager");

            var keys = dash.Widgets.Select(w => w.WidgetKey).ToList();
            keys.Should().Contain(new[]
            {
                "team-size", "team-attendance-today", "pending-approvals", "team-leave-calendar", "quick-actions",
            });

            Widget(dash, "team-size").Value.Should().Be(8);     // direct reports only (OTHER excluded)
            Widget(dash, "pending-approvals").Value.Should().Be(2); // BR-3 assigned-to-me queue
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Role selection — Employee (personal)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmployeeDashboard_ReturnsPersonalWidgetSet()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        var userId = Guid.NewGuid();
        await SeedEmployee(_tenantA, "EMP", dept, userId: userId);

        // upcoming-holidays via the holiday service stub (next 30 days).
        var holidays = Substitute.For<IHolidayService>();
        holidays.GetAllAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), null, null, true, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<HolidayDto>>.Success(
            [
                new HolidayDto { Name = "Independence Day", Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) },
            ]));

        // No HR perm, no direct reports, no Manager role → "employee".
        var (db, svc, _) = Scope(
            _tenantA, userId, [PermissionCatalog.Employee.ViewOwn], roles: [PermissionCatalog.BuiltInRoles.Employee],
            holidays: holidays);
        using (db)
        {
            var result = await svc.GetWidgetsAsync();

            result.IsSuccess.Should().BeTrue();
            var dash = result.Value!;
            dash.Role.Should().Be("employee");
            dash.GreetingName.Should().Be("EMP");

            var keys = dash.Widgets.Select(w => w.WidgetKey).ToList();
            // upcoming-holidays + pending-actions always present; leave-balance/attendance/onboarding/payslips
            // depend on optional collaborators (absent here) and degrade gracefully.
            keys.Should().Contain("upcoming-holidays");
            keys.Should().Contain("pending-actions");
            // No HR/manager widgets leak onto the employee dashboard.
            keys.Should().NotContain("headcount");
            keys.Should().NotContain("team-size");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BR-2 trend: 2 months of headcount joiners → correct direction + %
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Headcount_Trend_ComparesThisMonthVsLastMonthJoiners()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        var now = DateTime.UtcNow;
        var thisStart = new DateTime(now.Year, now.Month, 1);
        var lastStart = thisStart.AddMonths(-1);

        // Last month: 4 joiners. This month: 6 joiners. Growth → "up" + positive (green), +50%.
        for (int i = 0; i < 4; i++)
            await SeedEmployee(_tenantA, $"L{i}", dept, joining: lastStart.AddDays(2));
        for (int i = 0; i < 6; i++)
            await SeedEmployee(_tenantA, $"T{i}", dept, joining: thisStart.AddDays(2));

        var (db, svc, _) = Scope(_tenantA, Guid.NewGuid(), HrPerms());
        using (db)
        {
            var result = await svc.GetWidgetsAsync();

            result.IsSuccess.Should().BeTrue();
            var headcount = Widget(result.Value!, "headcount");
            headcount.Value.Should().Be(10m);               // total headcount
            headcount.PreviousValue.Should().Be(4m);        // last-month joiners
            headcount.TrendDirection.Should().Be("up");
            headcount.TrendPercentage.Should().Be(50.0m);   // (6-4)/4*100
            headcount.TrendIsPositive.Should().BeTrue();    // headcount growth = green (semantic, §8)
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  AC-5 tenant isolation
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Dashboard_TenantA_DoesNotSeeTenantBData()
    {
        var deptA = await SeedDepartment(_tenantA, "Engineering");
        var deptB = await SeedDepartment(_tenantB, "Sales");
        // Tenant A: 3 employees. Tenant B: 7 employees.
        for (int i = 0; i < 3; i++) await SeedEmployee(_tenantA, $"A{i}", deptA);
        for (int i = 0; i < 7; i++) await SeedEmployee(_tenantB, $"B{i}", deptB);

        var (dbA, svcA, _) = Scope(_tenantA, Guid.NewGuid(), HrPerms());
        using (dbA)
        {
            var a = await svcA.GetWidgetsAsync();
            a.IsSuccess.Should().BeTrue();
            Widget(a.Value!, "headcount").Value.Should().Be(3m); // ONLY Tenant A's employees
        }

        var (dbB, svcB, _) = Scope(_tenantB, Guid.NewGuid(), HrPerms());
        using (dbB)
        {
            var b = await svcB.GetWidgetsAsync();
            b.IsSuccess.Should().BeTrue();
            Widget(b.Value!, "headcount").Value.Should().Be(7m); // ONLY Tenant B's employees
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-285(a): upcoming-birthdays widget — index-backed SQL window.
    //  All use a FakeTimeProvider so "today" is deterministic (the window depends
    //  on the date). Joining dates are set OUTSIDE the window so the anniversary
    //  arm doesn't add noise; each test asserts the exact birthday label set.
    // ════════════════════════════════════════════════════════════════════════

    private static TimeProvider At(int year, int month, int day)
        => new FakeTimeProvider(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));

    // A mid-year joining date, far from any window used below (keeps anniversaries out of the assertions).
    private static readonly DateTime JoinFarFromWindow = new(2020, 8, 15);

    private IReadOnlyList<string> BirthdayLabels(DashboardResponse dash)
        => Widget(dash, "upcoming-birthdays").Items!
            .Select(i => i.Label!)
            .Where(l => l.EndsWith("— Birthday", StringComparison.Ordinal))
            .ToList();

    [Fact]
    [Trait("TC", "TC-RPT-005-13")]
    public async Task UpcomingBirthdays_IncludesInsideWindow_ExcludesOutside()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        // "today" = 2026-06-17; window = [06-17 .. 06-24] inclusive.
        await SeedEmployee(_tenantA, "TODAY", dept, joining: JoinFarFromWindow, dob: new DateTime(1985, 6, 17)); // boundary: today
        await SeedEmployee(_tenantA, "MID", dept, joining: JoinFarFromWindow, dob: new DateTime(1990, 6, 20));   // +3 days
        await SeedEmployee(_tenantA, "DAY7", dept, joining: JoinFarFromWindow, dob: new DateTime(1980, 6, 24));  // +7 days (inclusive)
        await SeedEmployee(_tenantA, "DAY8", dept, joining: JoinFarFromWindow, dob: new DateTime(1990, 6, 25));  // +8 days → out
        await SeedEmployee(_tenantA, "PAST", dept, joining: JoinFarFromWindow, dob: new DateTime(1990, 6, 10));  // already passed → out
        await SeedEmployee(_tenantA, "NODOB", dept, joining: JoinFarFromWindow, dob: null);                       // null DOB → out

        var (db, svc, _) = Scope(_tenantA, Guid.NewGuid(), HrPerms(), timeProvider: At(2026, 6, 17));
        using (db)
        {
            var dash = (await svc.GetWidgetsAsync()).Value!;
            BirthdayLabels(dash).Should().BeEquivalentTo(
                "TODAY X — Birthday", "MID X — Birthday", "DAY7 X — Birthday");
        }
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-13")]
    public async Task UpcomingBirthdays_WrapsYearEnd_IncludesEarlyJanuaryBirthday()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        // "today" = 2026-12-30; window = [12-30, 12-31, 01-01 .. 01-06] inclusive.
        await SeedEmployee(_tenantA, "DEC31", dept, joining: JoinFarFromWindow, dob: new DateTime(1980, 12, 31)); // +1 day
        await SeedEmployee(_tenantA, "JAN2", dept, joining: JoinFarFromWindow, dob: new DateTime(1990, 1, 2));    // wraps → +3 days
        await SeedEmployee(_tenantA, "JAN6", dept, joining: JoinFarFromWindow, dob: new DateTime(1995, 1, 6));    // wraps → +7 days (inclusive)
        await SeedEmployee(_tenantA, "JAN10", dept, joining: JoinFarFromWindow, dob: new DateTime(1990, 1, 10));  // +11 days → out
        await SeedEmployee(_tenantA, "DEC20", dept, joining: JoinFarFromWindow, dob: new DateTime(1990, 12, 20)); // already passed → out

        var (db, svc, _) = Scope(_tenantA, Guid.NewGuid(), HrPerms(), timeProvider: At(2026, 12, 30));
        using (db)
        {
            var dash = (await svc.GetWidgetsAsync()).Value!;
            BirthdayLabels(dash).Should().BeEquivalentTo(
                "DEC31 X — Birthday", "JAN2 X — Birthday", "JAN6 X — Birthday");
        }
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-13")]
    public async Task UpcomingBirthdays_ExcludesTerminatedAndInactive_IncludesProbation()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        // "today" = 2026-06-17; all DOBs land inside the window — only status filters them.
        await SeedEmployee(_tenantA, "ACT", dept, status: EmployeeStatus.Active, joining: JoinFarFromWindow, dob: new DateTime(1990, 6, 18));
        await SeedEmployee(_tenantA, "PROB", dept, status: EmployeeStatus.Probation, joining: JoinFarFromWindow, dob: new DateTime(1990, 6, 19));
        await SeedEmployee(_tenantA, "TERM", dept, status: EmployeeStatus.Terminated, joining: JoinFarFromWindow, dob: new DateTime(1990, 6, 20));
        await SeedEmployee(_tenantA, "SUSP", dept, status: EmployeeStatus.Suspended, joining: JoinFarFromWindow, dob: new DateTime(1990, 6, 21));

        var (db, svc, _) = Scope(_tenantA, Guid.NewGuid(), HrPerms(), timeProvider: At(2026, 6, 17));
        using (db)
        {
            var dash = (await svc.GetWidgetsAsync()).Value!;
            BirthdayLabels(dash).Should().BeEquivalentTo("ACT X — Birthday", "PROB X — Birthday");
        }
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-13")]
    public async Task BirthMonthDayInterceptor_SetsKeyOnCreate_AndUpdatesWhenDobChanges()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        var id = await SeedEmployee(_tenantA, "BMD", dept, dob: new DateTime(1990, 3, 5)); // Mar 5 → 305

        using (var db = Db(_tenantA))
        {
            var created = await db.Employees.SingleAsync(e => e.Id == id);
            created.BirthMonthDay.Should().Be(305);
        }

        // Change DOB → the interceptor must recompute the key on update.
        using (var db = Db(_tenantA))
        {
            var e = await db.Employees.SingleAsync(x => x.Id == id);
            e.DateOfBirth = new DateTime(1990, 11, 20); // Nov 20 → 1120
            await db.SaveChangesAsync();
        }

        using (var db = Db(_tenantA))
        {
            (await db.Employees.SingleAsync(e => e.Id == id)).BirthMonthDay.Should().Be(1120);
        }

        // Clear DOB → key becomes null.
        using (var db = Db(_tenantA))
        {
            var e = await db.Employees.SingleAsync(x => x.Id == id);
            e.DateOfBirth = null;
            await db.SaveChangesAsync();
        }

        using (var db = Db(_tenantA))
        {
            (await db.Employees.SingleAsync(e => e.Id == id)).BirthMonthDay.Should().BeNull();
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static DashboardWidget Widget(DashboardResponse dash, string key)
        => dash.Widgets.Single(w => w.WidgetKey == key);
}
