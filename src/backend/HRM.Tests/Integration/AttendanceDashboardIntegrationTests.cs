// ============================================================================
// US-ATT-010: Attendance dashboard + reports for HR — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - Dashboard KPIs: clock some in, some on leave, a holiday -> correct expected/clockedIn/onLeave/
//     absent/percent (AC-1/FR-1, BR-1/BR-2).
//   - Live board statuses: CLOCKED_IN / NOT_CLOCKED_IN / ON_LEAVE / HOLIDAY (AC-2/FR-2).
//   - Department comparison: per-department attendance rate (AC-3/FR-3).
//   - Custom report rollup over a range + a non-empty export per format csv/xlsx/pdf (AC-4/FR-4, FR-5).
//   - Trends: 12-month series from seeded monthly summaries (AC-5/FR-6, BR-5).
//   - Scheduled report config CRUD (FR-8).
//   - Team-scope narrowing to direct reports (BR-4).
//   - Multi-tenant isolation: Tenant A's dashboard excludes Tenant B (NFR-4 via EF global filters).
//
// NOTE ON PROVIDER: identical rationale to the other Attendance integration tests — the verify gate
// runs `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent sandbox, so a
// Testcontainers test would red the gate. These tests use the InMemory provider through the real
// composed pipeline (MediatR + tenant query filters), which is what proves tenant isolation.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Attendance.Queries;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class AttendanceDashboardIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _deptEng = Guid.NewGuid();
    private readonly Guid _deptSales = Guid.NewGuid();
    private readonly Guid _shiftId = Guid.NewGuid();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private Guid _managerEmpId;
    private Guid _empA1, _empA2, _empA3, _empB1;

    // A fully-completed month in the past so MonthBounds includes every day.
    private const int Year = 2026;
    private const int Month = 4;   // April 2026 (30 days)

    public AttendanceDashboardIntegrationTests()
    {
        SeedBase();
    }

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
        public Guid UserId { get; init; }
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions { get; init; } = [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private (IMediator Mediator, IServiceProvider Provider) BuildPipeline(
        Guid tenantId, Guid? userId = null, IReadOnlyList<string>? permissions = null)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser
        {
            UserId = userId ?? Guid.NewGuid(),
            TenantId = tenantId,
            Permissions = permissions ?? new[] { PermissionCatalog.Attendance.ViewAll },
        });
        services.AddSingleton<IReportExportStorage, InMemoryExportStorage>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IAttendanceDashboardService, AttendanceDashboardService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetDashboardKpisQuery).Assembly));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMediator>(), provider);
    }

    private sealed class InMemoryExportStorage : IReportExportStorage
    {
        public Task<string> SaveAsync(Guid tenantId, Guid reportId, string fileName,
            string contentType, byte[] content, CancellationToken cancellationToken = default)
            => Task.FromResult($"mem://{tenantId}/{reportId}/{fileName}");
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private void SeedBase()
    {
        using var db = Db(_tenantA);
        _managerEmpId = Guid.NewGuid();
        _empA1 = Guid.NewGuid(); _empA2 = Guid.NewGuid(); _empA3 = Guid.NewGuid(); _empB1 = Guid.NewGuid();

        db.Departments.AddRange(
            new Department { Id = _deptEng, TenantId = _tenantA, Name = "Engineering", Code = "ENG" },
            new Department { Id = _deptSales, TenantId = _tenantA, Name = "Sales", Code = "SAL" });

        db.Shifts.Add(new Shift
        {
            Id = _shiftId, TenantId = _tenantA, Name = "General",
            Type = ShiftType.Single,
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
            BreakDurationMinutes = 0, GracePeriodMinutes = 15,
            WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
            IsDefault = true, IsActive = true,
        });

        // Manager (linked to a user) + three reports in Engineering; A3 reports to the manager.
        var manager = Emp(_managerEmpId, _tenantA, _deptEng, "MGR");
        manager.UserId = _managerUserId;

        db.Employees.AddRange(
            manager,
            EmpReporting(_empA1, _tenantA, _deptEng, "A1", _managerEmpId),
            EmpReporting(_empA2, _tenantA, _deptSales, "A2", null),
            EmpReporting(_empA3, _tenantA, _deptEng, "A3", _managerEmpId),
            Emp(_empB1, _tenantB, Guid.NewGuid(), "B1"));

        db.SaveChanges();
    }

    private static Employee Emp(Guid id, Guid tenantId, Guid deptId, string name) => new()
    {
        Id = id, TenantId = tenantId,
        EmployeeNo = name, FirstName = name, LastName = "X", Email = $"{name}@t.com",
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = deptId, JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
    };

    private static Employee EmpReporting(Guid id, Guid tenantId, Guid deptId, string name, Guid? managerId)
    {
        var e = Emp(id, tenantId, deptId, name);
        e.ReportsToEmployeeId = managerId;
        return e;
    }

    private void SeedSettings(Guid tenantId)
    {
        using var db = Db(tenantId);
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            StandardWorkMinutes = 480, MinimumWorkMinutes = 240, HalfDayEnabled = false,
        });
        db.SaveChanges();
    }

    private void SeedLog(Guid tenantId, Guid employeeId, DateOnly date,
        int startHour, int startMinute, int workMinutes)
    {
        using var db = Db(tenantId);
        var clockIn = new DateTime(date.Year, date.Month, date.Day, startHour, startMinute, 0, DateTimeKind.Utc);
        var clockOut = clockIn.AddMinutes(workMinutes);

        var shift = db.Shifts.FirstOrDefault(s => s.IsDefault);
        var lateEarly = shift is null
            ? default
            : LateEarlyCalculator.Evaluate(
                clockInTime: TimeOnly.FromDateTime(clockIn),
                clockOutTime: TimeOnly.FromDateTime(clockOut),
                shiftStart: shift.StartTime,
                shiftEnd: shift.EndTime,
                gracePeriodMinutes: shift.GracePeriodMinutes,
                workedMinutes: workMinutes,
                minimumMinutes: shift.MinimumHours is { } mh ? (int)Math.Round(mh * 60m) : 0);

        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            ClockIn = clockIn, ClockOut = clockOut, TotalWorkMinutes = workMinutes,
            OvertimeMinutes = 0, Status = "COMPLETE", Source = "WEB",
            IsLate = lateEarly.IsLate, LateMinutes = lateEarly.LateMinutes, LateByMinutes = lateEarly.LateByMinutes,
            IsEarlyDeparture = lateEarly.IsEarlyDeparture, EarlyDepartureMinutes = lateEarly.EarlyDepartureMinutes,
        });
        db.SaveChanges();
    }

    private void SeedApprovedLeave(Guid tenantId, Guid employeeId, DateOnly start, DateOnly end)
    {
        using var db = Db(tenantId);
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            LeaveTypeId = Guid.NewGuid(), StartDate = start, EndDate = end,
            TotalDays = (end.DayNumber - start.DayNumber) + 1, Status = LeaveRequestStatus.Approved,
        });
        db.SaveChanges();
    }

    private void SeedHoliday(Guid tenantId, DateOnly date)
    {
        using var db = Db(tenantId);
        db.Holidays.Add(new Holiday
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Holiday",
            Date = date, Type = HolidayType.Public, IsActive = true,
        });
        db.SaveChanges();
    }

    private void SeedSummary(Guid tenantId, Guid employeeId, string yearMonth,
        decimal present, decimal absent, int late, int overtimeMinutes)
    {
        using var db = Db(tenantId);
        db.AttendanceMonthlySummaries.Add(new AttendanceMonthlySummary
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            YearMonth = yearMonth,
            TotalPresentDays = present, TotalAbsentDays = absent,
            TotalLateCount = late, TotalOvertimeMinutes = overtimeMinutes,
            GeneratedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    // A specific working weekday in the test month.
    private static DateOnly Weekday(int index)
    {
        var d = new DateOnly(Year, Month, 1);
        int found = 0;
        while (true)
        {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                if (found == index) return d;
                found++;
            }
            d = d.AddDays(1);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Dashboard KPIs (AC-1/FR-1, BR-1/BR-2)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Kpis_CountsClockedInLeaveHolidayAndPercent()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);
        // Of the 4 tenant-A employees (MGR, A1, A2, A3): A1 clocks in, A2 on full-day leave, A3 no log.
        SeedLog(_tenantA, _empA1, day, 9, 0, 480);
        SeedApprovedLeave(_tenantA, _empA2, day, day);

        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetDashboardKpisQuery(day, "all"));

        result.IsSuccess.Should().BeTrue();
        var kpi = result.Value!;
        kpi.OnLeave.Should().Be(1);
        kpi.ClockedIn.Should().Be(1);
        // expected = 4 active - 1 on leave - 0 holiday = 3.
        kpi.ExpectedHeadcount.Should().Be(3);
        kpi.PendingClockIn.Should().Be(2);   // MGR + A3 not in.
        kpi.Absent.Should().Be(2);
        // percent = 1/3 * 100 = 33.33
        kpi.AttendancePercent.Should().BeApproximately(33.33m, 0.01m);
    }

    [Fact]
    public async Task Kpis_HolidayReducesExpectedHeadcount()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);
        SeedHoliday(_tenantA, day);   // tenant-wide holiday

        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetDashboardKpisQuery(day, "all"));

        result.IsSuccess.Should().BeTrue();
        // All 4 are on a holiday -> expected excludes them -> 0.
        result.Value!.ExpectedHeadcount.Should().Be(0);
        result.Value.AttendancePercent.Should().Be(0m);
    }

    // ════════════════════════════════════════════════════════════════
    //  Live board (AC-2/FR-2)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LiveBoard_ReportsPerEmployeeStatuses()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);
        SeedLog(_tenantA, _empA1, day, 9, 0, 480);
        SeedApprovedLeave(_tenantA, _empA2, day, day);

        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetLiveBoardQuery(day, "all"));

        result.IsSuccess.Should().BeTrue();
        var rows = result.Value!.Rows;
        rows.Single(r => r.EmployeeId == _empA1).Status.Should().Be("CLOCKED_IN");
        rows.Single(r => r.EmployeeId == _empA1).ClockInAt.Should().NotBeNull();
        rows.Single(r => r.EmployeeId == _empA2).Status.Should().Be("ON_LEAVE");
        rows.Single(r => r.EmployeeId == _empA3).Status.Should().Be("NOT_CLOCKED_IN");
    }

    [Fact]
    public async Task LiveBoard_HolidayStatus()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);
        SeedHoliday(_tenantA, day);

        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetLiveBoardQuery(day, "all"));

        result.Value!.Rows.Should().OnlyContain(r => r.Status == "HOLIDAY");
    }

    // ════════════════════════════════════════════════════════════════
    //  Team-scope narrowing (BR-4)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LiveBoard_TeamScope_OnlyDirectReports()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);

        // Acting user is the manager; team scope should show only A1 + A3 (their direct reports).
        var (mediator, _) = BuildPipeline(_tenantA, userId: _managerUserId);
        var result = await mediator.Send(new GetLiveBoardQuery(day, "team"));

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value!.Rows.Select(r => r.EmployeeId).ToHashSet();
        ids.Should().BeEquivalentTo(new[] { _empA1, _empA3 });
        ids.Should().NotContain(_empA2);   // Sales, not a report.
        ids.Should().NotContain(_managerEmpId);
    }

    [Fact]
    public async Task Kpis_AllScope_RequiresViewAllPermission()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);

        // No permissions -> scope=all is forbidden (BR-3).
        var (mediator, _) = BuildPipeline(_tenantA, permissions: Array.Empty<string>());
        var result = await mediator.Send(new GetDashboardKpisQuery(day, "all"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("scope_not_allowed");
    }

    // ════════════════════════════════════════════════════════════════
    //  Department comparison (AC-3/FR-3)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DepartmentComparison_ComputesRatePerDepartment()
    {
        SeedSettings(_tenantA);
        // ENG members (MGR, A1, A3) present every working day -> ENG ~100%.
        // SALES member (A2) absent the first 5 working days -> SALES lower.
        var workingDays = Enumerable.Range(0, 22).Select(Weekday).Distinct().ToList();
        foreach (var wd in workingDays)
        {
            SeedLog(_tenantA, _managerEmpId, wd, 9, 0, 480);   // ENG
            SeedLog(_tenantA, _empA1, wd, 9, 0, 480);          // ENG
            SeedLog(_tenantA, _empA3, wd, 9, 0, 480);          // ENG
        }
        foreach (var wd in workingDays.Skip(5)) SeedLog(_tenantA, _empA2, wd, 9, 0, 480);   // SALES, 5 absent

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetDepartmentComparisonQuery(Year, Month));

        result.IsSuccess.Should().BeTrue();
        var eng = result.Value!.Rows.FirstOrDefault(r => r.DepartmentId == _deptEng);
        var sales = result.Value.Rows.FirstOrDefault(r => r.DepartmentId == _deptSales);
        eng.Should().NotBeNull();
        sales.Should().NotBeNull();
        eng!.AttendanceRatePct.Should().BeGreaterThan(sales!.AttendanceRatePct);
    }

    // ════════════════════════════════════════════════════════════════
    //  Custom report (AC-4/FR-4) + export (FR-5)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CustomReport_RollsUpPresentLateOvertimeWork()
    {
        SeedSettings(_tenantA);
        var d1 = Weekday(0); var d2 = Weekday(1); var d3 = Weekday(2);
        SeedLog(_tenantA, _empA1, d1, 9, 0, 480);
        SeedLog(_tenantA, _empA1, d2, 9, 30, 360);   // late (after 09:15 grace)

        var from = d1; var to = d3;
        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetCustomReportQuery(from, to, new CustomReportFilter()));

        result.IsSuccess.Should().BeTrue();
        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.PresentDays.Should().Be(2m);
        row.LateCount.Should().Be(1);
        row.WorkMinutes.Should().Be(840);
        // 3 working days in range, 2 present -> 1 absent.
        row.AbsentDays.Should().Be(1m);
    }

    [Fact]
    public async Task CustomReport_ScheduledWorkingDays_BatchResolution_AssignedAndDefaultShift_BUG125()
    {
        // BUG-125: scheduled-working-days resolution was hoisted OUT of the per-employee loop into a batch
        // (fixed query count regardless of employee count). The per-employee scheduled count — surfaced here
        // as AbsentDays (= scheduled − present − leave; with no logs/leave, AbsentDays == scheduled) — must
        // be identical to the former per-employee resolution for both an assigned non-default shift and the
        // default-shift fallback.
        SeedSettings(_tenantA);

        var sixDayShift = Guid.NewGuid();
        var empSix = Guid.NewGuid();   // Mon–Sat via an explicit assignment
        using (var db = Db(_tenantA))
        {
            db.Shifts.Add(new Shift
            {
                Id = sixDayShift, TenantId = _tenantA, Name = "Six-Day",
                Type = ShiftType.Single,
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
                BreakDurationMinutes = 0, GracePeriodMinutes = 15,
                WorkingDays = new List<int> { 1, 2, 3, 4, 5, 6 },   // Mon–Sat
                IsDefault = false, IsActive = true,
            });
            db.Employees.Add(Emp(empSix, _tenantA, _deptEng, "A-SIX"));
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA,
                EmployeeId = empSix, ShiftId = sixDayShift,
                EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = null,
            });
            db.SaveChanges();
        }

        // A full Mon–Sun week (Apr 6 = Monday .. Apr 12 = Sunday). No logs / no leave, so every scheduled
        // day is absent → AbsentDays == the shift's scheduled working days over the range.
        var from = new DateOnly(2026, 4, 6);
        var to = new DateOnly(2026, 4, 12);

        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetCustomReportQuery(from, to, new CustomReportFilter()));
        result.IsSuccess.Should().BeTrue();

        int Expected(HashSet<int> workingWeekdays)
        {
            int c = 0;
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                int iso = (int)d.DayOfWeek == 0 ? 7 : (int)d.DayOfWeek;
                if (workingWeekdays.Count == 0 || workingWeekdays.Contains(iso)) c++;
            }
            return c;
        }
        var monFri = new HashSet<int> { 1, 2, 3, 4, 5 };
        var monSat = new HashSet<int> { 1, 2, 3, 4, 5, 6 };

        // _empA1 has no assignment → default Mon–Fri; empSix → assigned Mon–Sat.
        var defaultRow = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        var sixRow = result.Value.Rows.Single(r => r.EmployeeId == empSix);

        defaultRow.AbsentDays.Should().Be(Expected(monFri));   // 5 (Mon–Fri)
        sixRow.AbsentDays.Should().Be(Expected(monSat));       // 6 (Mon–Sat)
        sixRow.AbsentDays.Should().BeGreaterThan(defaultRow.AbsentDays);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    public async Task ExportCustomReport_ProducesNonEmptyFile(string format)
    {
        SeedSettings(_tenantA);
        var d1 = Weekday(0);
        SeedLog(_tenantA, _empA1, d1, 9, 0, 480);

        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new ExportCustomReportQuery(d1, d1, format, new CustomReportFilter()));

        result.IsSuccess.Should().BeTrue();
        result.Value!.FileContent.Should().NotBeNullOrEmpty();
        result.Value.FileName.Should().EndWith($".{format}");
    }

    [Fact]
    public async Task ExportCustomReport_InvalidFormat_Fails()
    {
        SeedSettings(_tenantA);
        var d1 = Weekday(0);
        var (mediator, _) = BuildPipeline(_tenantA);

        var result = await mediator.Send(new ExportCustomReportQuery(d1, d1, "docx", new CustomReportFilter()));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_format");
    }

    [Fact]
    public async Task CustomReport_InvalidRange_Fails()
    {
        SeedSettings(_tenantA);
        var (mediator, _) = BuildPipeline(_tenantA);
        var d1 = Weekday(2);

        var result = await mediator.Send(new GetCustomReportQuery(d1, d1.AddDays(-2), new CustomReportFilter()));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_range");
    }

    // ════════════════════════════════════════════════════════════════
    //  Trends (AC-5/FR-6, BR-5 — from the monthly summary)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Trends_BuildsTwelveMonthSeriesFromSummaries()
    {
        SeedSettings(_tenantA);
        // Seed a summary in the current month so it lands in the 12-month window.
        var now = DateTime.UtcNow;
        var ym = $"{now.Year:D4}-{now.Month:D2}";
        SeedSummary(_tenantA, _empA1, ym, present: 18m, absent: 2m, late: 3, overtimeMinutes: 120);

        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetTrendsQuery(12));

        result.IsSuccess.Should().BeTrue();
        result.Value!.AttendanceRate.Should().HaveCount(12);
        result.Value.LateArrivals.Should().HaveCount(12);
        result.Value.OvertimeHours.Should().HaveCount(12);
        result.Value.AbsenteeismRate.Should().HaveCount(12);

        var current = result.Value.AttendanceRate.Single(p => p.Period == ym);
        current.Value.Should().BeApproximately(90m, 0.01m);   // 18 / 20 * 100
        result.Value.LateArrivals.Single(p => p.Period == ym).Value.Should().Be(3m);
        result.Value.OvertimeHours.Single(p => p.Period == ym).Value.Should().Be(2m);   // 120 min
        result.Value.AbsenteeismRate.Single(p => p.Period == ym).Value.Should().BeApproximately(10m, 0.01m);
    }

    // ════════════════════════════════════════════════════════════════
    //  Scheduled report config CRUD (FR-8)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScheduledReport_CreateListUpdateDelete()
    {
        var (mediator, _) = BuildPipeline(_tenantA);

        var dto = new ScheduledReportConfigDto
        {
            ReportType = "CUSTOM",
            Frequency = "WEEKLY",
            Format = "XLSX",
            DeliveryTime = "08:30",
            Recipients = new[] { Guid.NewGuid() },
            Filters = new Dictionary<string, object?> { ["departmentId"] = "eng" },
            IsActive = true,
        };

        var created = await mediator.Send(new CreateScheduledReportCommand(dto));
        created.IsSuccess.Should().BeTrue();
        created.Value!.Id.Should().NotBeNull();
        created.Value.Frequency.Should().Be("WEEKLY");
        created.Value.DeliveryTime.Should().Be("08:30");

        var list = await mediator.Send(new GetScheduledReportsQuery());
        list.Value!.Should().ContainSingle(c => c.Id == created.Value.Id);

        var updateDto = dto with { Frequency = "MONTHLY", IsActive = false };
        var updated = await mediator.Send(new UpdateScheduledReportCommand(created.Value.Id!.Value, updateDto));
        updated.IsSuccess.Should().BeTrue();
        updated.Value!.Frequency.Should().Be("MONTHLY");
        updated.Value.IsActive.Should().BeFalse();

        var deleted = await mediator.Send(new DeleteScheduledReportCommand(created.Value.Id!.Value));
        deleted.IsSuccess.Should().BeTrue();

        var afterDelete = await mediator.Send(new GetScheduledReportsQuery());
        afterDelete.Value!.Should().NotContain(c => c.Id == created.Value.Id);
    }

    [Fact]
    public async Task ScheduledReport_InvalidFrequency_Fails()
    {
        var (mediator, _) = BuildPipeline(_tenantA);
        var dto = new ScheduledReportConfigDto
        {
            ReportType = "CUSTOM", Frequency = "YEARLY", Format = "CSV", DeliveryTime = "08:00",
        };

        var result = await mediator.Send(new CreateScheduledReportCommand(dto));
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_frequency");
    }

    [Fact]
    public async Task ScheduledReport_Update_NotFound()
    {
        var (mediator, _) = BuildPipeline(_tenantA);
        var dto = new ScheduledReportConfigDto
        {
            ReportType = "CUSTOM", Frequency = "DAILY", Format = "CSV", DeliveryTime = "08:00",
        };

        var result = await mediator.Send(new UpdateScheduledReportCommand(Guid.NewGuid(), dto));
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ════════════════════════════════════════════════════════════════
    //  Multi-tenant isolation (NFR-4 via EF global filters)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LiveBoard_TenantA_ExcludesTenantBEmployees()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);
        SeedLog(_tenantA, _empA1, day, 9, 0, 480);
        SeedLog(_tenantB, _empB1, day, 9, 0, 480);

        var (mediatorA, _) = BuildPipeline(_tenantA);
        var result = await mediatorA.Send(new GetLiveBoardQuery(day, "all"));

        result.Value!.Rows.Should().Contain(r => r.EmployeeId == _empA1);
        result.Value.Rows.Should().NotContain(r => r.EmployeeId == _empB1);
    }

    [Fact]
    public async Task ScheduledReport_TenantA_DoesNotSeeTenantBConfigs()
    {
        var (mediatorB, _) = BuildPipeline(_tenantB);
        await mediatorB.Send(new CreateScheduledReportCommand(new ScheduledReportConfigDto
        {
            ReportType = "CUSTOM", Frequency = "DAILY", Format = "CSV", DeliveryTime = "08:00",
        }));

        var (mediatorA, _) = BuildPipeline(_tenantA);
        var list = await mediatorA.Send(new GetScheduledReportsQuery());

        list.Value!.Should().BeEmpty();
    }
}
