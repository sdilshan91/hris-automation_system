// ============================================================================
// US-ATT-007: Monthly attendance summary per employee — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - Full-month aggregation with varied data (present/absent/late/early/overtime/leave/holiday/weekly-off).
//   - LOP calc: 3 absent working days with no leave => lop_days = 3 (BR-3).
//   - Half-day: 4h on an 8h shift with HalfDayEnabled => 0.5 present (BR-5); disabled => full present.
//   - Leave reconciliation: approved leave day is not absent (BR-6).
//   - Holiday exclusion: a public holiday on a working day is neither present nor absent (BR-4).
//   - Late / early counts from the resolved shift + grace (FR-3, US-ATT-008-pending).
//   - On-demand generation persists rows (AC-3/FR-4).
//   - Department filter (AC-5/FR-5).
//   - Export produces a non-empty file per format csv/xlsx/pdf (AC-4/FR-6).
//   - Multi-tenant isolation: Tenant A's summary excludes Tenant B (NFR-3 via EF global filters).
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
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class MonthlySummaryIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _deptEng = Guid.NewGuid();
    private readonly Guid _deptSales = Guid.NewGuid();
    private readonly Guid _shiftId = Guid.NewGuid();

    private Guid _empA1, _empA2, _empB1;

    // A fully-completed month in the past so MonthBounds includes every day.
    private const int Year = 2026;
    private const int Month = 4;   // April 2026 (30 days)

    public MonthlySummaryIntegrationTests()
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

    private (IMediator Mediator, IServiceProvider Provider) BuildPipeline(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<IReportExportStorage, InMemoryExportStorage>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetMonthlySummaryQuery).Assembly));

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
        _empA1 = Guid.NewGuid(); _empA2 = Guid.NewGuid(); _empB1 = Guid.NewGuid();

        db.Departments.AddRange(
            new Department { Id = _deptEng, TenantId = _tenantA, Name = "Engineering", Code = "ENG" },
            new Department { Id = _deptSales, TenantId = _tenantA, Name = "Sales", Code = "SAL" });

        // A Mon–Fri 09:00–17:00 shift (8h span, 0 break => 480 standard), 15-min grace, default.
        db.Shifts.Add(new Shift
        {
            Id = _shiftId, TenantId = _tenantA, Name = "General",
            Type = ShiftType.Single,
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
            BreakDurationMinutes = 0, GracePeriodMinutes = 15,
            WorkingDays = new List<int> { 1, 2, 3, 4, 5 },   // Mon–Fri
            IsDefault = true, IsActive = true,
        });

        db.Employees.AddRange(
            Emp(_empA1, _tenantA, _deptEng, "A1"),
            Emp(_empA2, _tenantA, _deptSales, "A2"),
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

    private void SeedSettings(Guid tenantId, bool halfDayEnabled = false)
    {
        using var db = Db(tenantId);
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            StandardWorkMinutes = 480,
            MinimumWorkMinutes = 240,
            HalfDayEnabled = halfDayEnabled,
        });
        db.SaveChanges();
    }

    /// <summary>Adds a completed attendance log on the given date (UTC) with the given span/minutes.</summary>
    private void SeedLog(Guid tenantId, Guid employeeId, DateOnly date,
        int startHour, int startMinute, int workMinutes)
    {
        using var db = Db(tenantId);
        var clockIn = new DateTime(date.Year, date.Month, date.Day, startHour, startMinute, 0, DateTimeKind.Utc);
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            ClockIn = clockIn,
            ClockOut = clockIn.AddMinutes(workMinutes),
            TotalWorkMinutes = workMinutes,
            OvertimeMinutes = 0,
            Status = "COMPLETE",
            Source = "WEB",
        });
        db.SaveChanges();
    }

    private void SeedApprovedLeave(Guid tenantId, Guid employeeId, DateOnly start, DateOnly end)
    {
        using var db = Db(tenantId);
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            LeaveTypeId = Guid.NewGuid(),
            StartDate = start, EndDate = end,
            TotalDays = (end.DayNumber - start.DayNumber) + 1,
            Status = LeaveRequestStatus.Approved,
        });
        db.SaveChanges();
    }

    private void SeedHoliday(Guid tenantId, DateOnly date)
    {
        using var db = Db(tenantId);
        db.Holidays.Add(new Holiday
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            Name = "Holiday",
            Date = date,
            Type = HolidayType.Public,
            IsActive = true,
        });
        db.SaveChanges();
    }

    private void SeedApprovedOvertime(Guid tenantId, Guid employeeId, DateOnly date, int minutes)
    {
        using var db = Db(tenantId);
        db.OvertimeRecords.Add(new OvertimeRecord
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Date = date,
            OvertimeMinutes = minutes,
            ApprovedMinutes = minutes,
            Multiplier = 1.5m,
            Type = OvertimeType.AutoDetected,
            Status = OvertimeStatus.Approved,
            Reason = "ot",
            IsPayrollReady = true,
        });
        db.SaveChanges();
    }

    // Helper: the Nth weekday (Mon–Fri) of the test month, for predictable working-day seeding.
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

    private static MonthlySummaryFilter NoFilter => new();

    // ════════════════════════════════════════════════════════════════
    //  LOP calc (BR-3)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_ThreeAbsentWorkingDaysNoLeave_LopEqualsThree()
    {
        SeedSettings(_tenantA);
        // Present on 5 working days, absent on the rest (no logs). With Mon–Fri working days and no
        // leave, every working day with no log is absent + LOP. We assert 3 specific absences by
        // making the employee present on all working days EXCEPT 3.
        var workingDays = Enumerable.Range(0, 22).Select(Weekday).Distinct().ToList(); // April 2026 weekdays
        // Present on all but the first 3 working days.
        foreach (var wd in workingDays.Skip(3))
            SeedLog(_tenantA, _empA1, wd, 9, 0, 480);

        var (mediator, _) = BuildPipeline(_tenantA);
        var gen = await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        gen.IsSuccess.Should().BeTrue();

        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));
        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.AbsentDays.Should().Be(3m);
        row.LopDays.Should().Be(3m);
    }

    // ════════════════════════════════════════════════════════════════
    //  Leave reconciliation (BR-6)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_ApprovedLeaveDay_NotCountedAsAbsent()
    {
        SeedSettings(_tenantA);
        var leaveDay = Weekday(0);
        SeedApprovedLeave(_tenantA, _empA1, leaveDay, leaveDay);
        // Present every other working day.
        foreach (var wd in Enumerable.Range(1, 21).Select(Weekday))
            SeedLog(_tenantA, _empA1, wd, 9, 0, 480);

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.LeaveDays.Should().Be(1m);
        row.AbsentDays.Should().Be(0m);
        row.LopDays.Should().Be(0m);
    }

    // ════════════════════════════════════════════════════════════════
    //  Holiday exclusion (BR-4)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_PublicHolidayOnWorkingDay_NotPresentNorAbsent()
    {
        SeedSettings(_tenantA);
        var holiday = Weekday(0);
        SeedHoliday(_tenantA, holiday);
        // No log on the holiday; present on the rest.
        foreach (var wd in Enumerable.Range(1, 21).Select(Weekday))
            SeedLog(_tenantA, _empA1, wd, 9, 0, 480);

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.Holidays.Should().BeGreaterThanOrEqualTo(1);
        // The holiday working-day is excluded from absent (no log, but it's a holiday).
        row.AbsentDays.Should().Be(0m);
    }

    // ════════════════════════════════════════════════════════════════
    //  Half-day (BR-5)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_HalfDayEnabled_FourHoursOnEightHourShift_CountsHalfPresent()
    {
        SeedSettings(_tenantA, halfDayEnabled: true);
        var day = Weekday(0);
        SeedLog(_tenantA, _empA1, day, 9, 0, 240);   // 4h < 480 min, >= 50% of 480 => half day

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.PresentDays.Should().Be(0.5m);
    }

    [Fact]
    public async Task Generate_HalfDayDisabled_FourHours_CountsFullPresent()
    {
        SeedSettings(_tenantA, halfDayEnabled: false);
        var day = Weekday(0);
        SeedLog(_tenantA, _empA1, day, 9, 0, 240);

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.PresentDays.Should().Be(1m);
    }

    // ════════════════════════════════════════════════════════════════
    //  Late / early counts from shift (FR-3, US-ATT-008-pending)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_LateArrivalAndEarlyDeparture_AreCounted()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);
        // Shift start 09:00 + 15 grace => late after 09:15. Clock in 09:30 (late), clock out before 17:00.
        SeedLog(_tenantA, _empA1, day, 9, 30, 360);   // 09:30 -> 15:30 (early departure < 17:00)

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.LateCount.Should().Be(1);
        row.EarlyDepartureCount.Should().Be(1);
    }

    [Fact]
    public async Task Generate_OnTimeWithinGrace_NotLate()
    {
        SeedSettings(_tenantA);
        var day = Weekday(0);
        // Clock in 09:10 (within 15-min grace) -> not late; out at 17:00 -> not early.
        SeedLog(_tenantA, _empA1, day, 9, 10, 470);   // 09:10 -> 17:00

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.LateCount.Should().Be(0);
        row.EarlyDepartureCount.Should().Be(0);
    }

    // ════════════════════════════════════════════════════════════════
    //  Overtime = approved only; work minutes aggregate (NFR-5)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_ApprovedOvertimeAndWorkMinutes_Aggregated()
    {
        SeedSettings(_tenantA);
        var d1 = Weekday(0); var d2 = Weekday(1);
        SeedLog(_tenantA, _empA1, d1, 9, 0, 480);
        SeedLog(_tenantA, _empA1, d2, 9, 0, 500);
        SeedApprovedOvertime(_tenantA, _empA1, d1, 60);   // approved => counted
        // a pending OT should NOT be counted
        using (var db = Db(_tenantA))
        {
            db.OvertimeRecords.Add(new OvertimeRecord
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeId = _empA1, Date = d2,
                OvertimeMinutes = 99, Multiplier = 1.5m, Type = OvertimeType.AutoDetected,
                Status = OvertimeStatus.Pending, Reason = "x",
            });
            db.SaveChanges();
        }

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        var row = result.Value!.Rows.Single(r => r.EmployeeId == _empA1);
        row.WorkMinutes.Should().Be(980);
        row.OvertimeMinutes.Should().Be(60);   // approved only
    }

    // ════════════════════════════════════════════════════════════════
    //  On-demand generation persists rows (AC-3/FR-4)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_PersistsMaterializedRows()
    {
        SeedSettings(_tenantA);
        SeedLog(_tenantA, _empA1, Weekday(0), 9, 0, 480);

        var (mediator, provider) = BuildPipeline(_tenantA);
        var gen = await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));

        gen.IsSuccess.Should().BeTrue();
        gen.Value!.Status.Should().Be("COMPLETED");
        gen.Value.GeneratedAt.Should().NotBeNull();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceMonthlySummaries.Should().Contain(s =>
            s.EmployeeId == _empA1 && s.YearMonth == "2026-04");
    }

    [Fact]
    public async Task Generate_Twice_UpsertsNotDuplicates()
    {
        SeedSettings(_tenantA);
        SeedLog(_tenantA, _empA1, Weekday(0), 9, 0, 480);

        var (mediator, provider) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceMonthlySummaries.Count(s => s.EmployeeId == _empA1 && s.YearMonth == "2026-04")
            .Should().Be(1);
    }

    // ════════════════════════════════════════════════════════════════
    //  Department filter (AC-5/FR-5)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMonthly_DepartmentFilter_ReturnsOnlyThatDepartment()
    {
        SeedSettings(_tenantA);
        SeedLog(_tenantA, _empA1, Weekday(0), 9, 0, 480);   // Engineering
        SeedLog(_tenantA, _empA2, Weekday(0), 9, 0, 480);   // Sales

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));

        var result = await mediator.Send(new GetMonthlySummaryQuery(
            Year, Month, new MonthlySummaryFilter { DepartmentId = _deptEng }));

        result.Value!.Rows.Should().ContainSingle(r => r.EmployeeId == _empA1);
        result.Value.Rows.Should().NotContain(r => r.EmployeeId == _empA2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Daily breakdown (AC-2)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetBreakdown_ReturnsDayByDayStatuses()
    {
        SeedSettings(_tenantA);
        var present = Weekday(0);
        SeedLog(_tenantA, _empA1, present, 9, 0, 480);

        var (mediator, _) = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetEmployeeMonthlyBreakdownQuery(_empA1, Year, Month));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Days.Should().NotBeEmpty();
        result.Value.Days.Should().Contain(d =>
            d.Date == present.ToString("yyyy-MM-dd") && d.Status == "PRESENT");
        // A Saturday should be a weekly off.
        var saturday = new DateOnly(Year, Month, 4); // 2026-04-04 is a Saturday
        saturday.DayOfWeek.Should().Be(DayOfWeek.Saturday);
        result.Value.Days.Should().Contain(d =>
            d.Date == "2026-04-04" && d.Status == "WEEKLY_OFF");
    }

    // ════════════════════════════════════════════════════════════════
    //  Export (AC-4/FR-6)
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    public async Task Export_ProducesNonEmptyFile(string format)
    {
        SeedSettings(_tenantA);
        SeedLog(_tenantA, _empA1, Weekday(0), 9, 0, 480);

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));

        var result = await mediator.Send(new ExportMonthlySummaryQuery(Year, Month, format, NoFilter));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Queued.Should().BeFalse();
        result.Value.FileContent.Should().NotBeNullOrEmpty();
        result.Value.FileName.Should().EndWith($".{format}");
    }

    [Fact]
    public async Task Export_InvalidFormat_Fails()
    {
        SeedSettings(_tenantA);
        var (mediator, _) = BuildPipeline(_tenantA);

        var result = await mediator.Send(new ExportMonthlySummaryQuery(Year, Month, "docx", NoFilter));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_format");
    }

    // ════════════════════════════════════════════════════════════════
    //  Multi-tenant isolation (NFR-3 via EF global filters)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMonthly_TenantA_ExcludesTenantBEmployees()
    {
        SeedSettings(_tenantA);
        SeedSettings(_tenantB);
        SeedLog(_tenantA, _empA1, Weekday(0), 9, 0, 480);
        SeedLog(_tenantB, _empB1, Weekday(0), 9, 0, 480);

        var (mediatorA, _) = BuildPipeline(_tenantA);
        await mediatorA.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediatorA.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        result.Value!.Rows.Should().Contain(r => r.EmployeeId == _empA1);
        result.Value.Rows.Should().NotContain(r => r.EmployeeId == _empB1);
    }

    [Fact]
    public async Task Banner_ComputesAggregates()
    {
        SeedSettings(_tenantA);
        // A1 present all working days; A2 absent 2 days -> nonzero LOP for the banner total.
        var workingDays = Enumerable.Range(0, 22).Select(Weekday).Distinct().ToList();
        foreach (var wd in workingDays) SeedLog(_tenantA, _empA1, wd, 9, 0, 480);
        foreach (var wd in workingDays.Skip(2)) SeedLog(_tenantA, _empA2, wd, 9, 0, 480);

        var (mediator, _) = BuildPipeline(_tenantA);
        await mediator.Send(new GenerateMonthlySummaryCommand(Year, Month));
        var result = await mediator.Send(new GetMonthlySummaryQuery(Year, Month, NoFilter));

        result.Value!.Banner.TotalEmployees.Should().Be(2);
        result.Value.Banner.TotalLopDays.Should().Be(2m);
        result.Value.Banner.AverageAttendancePercent.Should().BeGreaterThan(0m);
        result.Value.GeneratedAt.Should().NotBeNull();
    }
}
