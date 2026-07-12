// ============================================================================
// US-ATT-009: Attendance ⇄ Payroll integration (ATTENDANCE SIDE) — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext global query filters), covering:
//   - Lock lifecycle: lock a period (AC-4/FR-3), GetPeriodLock surfaces the active lock, an
//     overlapping lock is rejected (NFR-2), unlock clears it (AC-5).
//   - Lock ENFORCEMENT: a clock-in whose date falls in a locked period is rejected with
//     'payroll_period_locked' (AC-4); unlocking re-allows it (AC-5).
//   - Payroll data pull (FR-1/FR-2): a row per employee with present/lop/overtime/work fed from the
//     US-ATT-007 summary.
//   - Multi-tenant isolation (NFR-3): Tenant B cannot see Tenant A's lock (EF global query filters).
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the other Attendance
// integration tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker).
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
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class AttendancePayrollIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private readonly Guid _empA = Guid.NewGuid();
    private readonly Guid _empB = Guid.NewGuid();
    private readonly Guid _shiftId = Guid.NewGuid();

    public AttendancePayrollIntegrationTests() => SeedBase();

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

    private sealed class InMemoryExportStorage : IReportExportStorage
    {
        public Task<string> SaveAsync(Guid tenantId, Guid reportId, string fileName,
            string contentType, byte[] content, CancellationToken cancellationToken = default)
            => Task.FromResult($"mem://{tenantId}/{reportId}/{fileName}");
    }

    private (IMediator Mediator, IServiceProvider Provider) BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Permissions.Returns(Array.Empty<string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IReportExportStorage, InMemoryExportStorage>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(LockPeriodCommand).Assembly));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMediator>(), provider);
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
        db.Shifts.Add(new Shift
        {
            Id = _shiftId, TenantId = _tenantA, Name = "General",
            Type = ShiftType.Single,
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
            BreakDurationMinutes = 0, GracePeriodMinutes = 15,
            WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
            IsDefault = true, IsActive = true,
        });
        db.Employees.AddRange(
            Emp(_empA, _tenantA, _userA, "A1"),
            Emp(_empB, _tenantB, _userB, "B1"));
        db.SaveChanges();
    }

    private static Employee Emp(Guid id, Guid tenantId, Guid userId, string name) => new()
    {
        Id = id, TenantId = tenantId, UserId = userId,
        EmployeeNo = name, FirstName = name, LastName = "X", Email = $"{name}@t.com",
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
    };

    /// <summary>Seeds a completed attendance log so the employee has REAL tracked data for the day.</summary>
    private void SeedLog(Guid tenantId, Guid employeeId, DateOnly date, int workMinutes = 480)
    {
        using var db = Db(tenantId);
        var clockIn = new DateTime(date.Year, date.Month, date.Day, 9, 0, 0, DateTimeKind.Utc);
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

    private static ClockInCommand ClockIn() => new(null, null, null, "WEB", "127.0.0.1", "test-agent", null);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ── Lock lifecycle (AC-4 / AC-5 / FR-3) ─────────────────────────────────

    [Fact]
    public async Task LockPeriod_ThenGetPeriodLock_ReturnsActiveLock()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var month = Today;

        var lockResult = await mediator.Send(
            new LockPeriodCommand(new DateOnly(month.Year, month.Month, 1),
                new DateOnly(month.Year, month.Month, 28)));
        lockResult.IsSuccess.Should().BeTrue();
        lockResult.Value!.IsLocked.Should().BeTrue();
        lockResult.Value.LockedBy.Should().Be(_userA);

        var status = await mediator.Send(new GetPeriodLockQuery(month.Year, month.Month));
        status.IsSuccess.Should().BeTrue();
        status.Value.Should().NotBeNull();
        status.Value!.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task LockPeriod_OverlappingActiveLock_IsRejected()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var m = Today;
        await mediator.Send(new LockPeriodCommand(
            new DateOnly(m.Year, m.Month, 1), new DateOnly(m.Year, m.Month, 15)));

        var overlap = await mediator.Send(new LockPeriodCommand(
            new DateOnly(m.Year, m.Month, 10), new DateOnly(m.Year, m.Month, 20)));

        overlap.IsSuccess.Should().BeFalse();
        overlap.StatusCode.Should().Be(409);
        overlap.ErrorCode.Should().Be("already_locked");
    }

    [Fact]
    public async Task Unlock_ClearsTheLock()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var m = Today;
        var locked = await mediator.Send(new LockPeriodCommand(
            new DateOnly(m.Year, m.Month, 1), new DateOnly(m.Year, m.Month, 28)));

        var unlocked = await mediator.Send(new UnlockPeriodCommand(locked.Value!.Id));

        unlocked.IsSuccess.Should().BeTrue();
        unlocked.Value!.IsLocked.Should().BeFalse();
        unlocked.Value.UnlockedBy.Should().Be(_userA);

        var status = await mediator.Send(new GetPeriodLockQuery(m.Year, m.Month));
        status.Value.Should().BeNull();   // no ACTIVE lock remains
    }

    // ── Lock enforcement on clock-in (AC-4 / AC-5) ──────────────────────────

    [Fact]
    public async Task ClockIn_WithinLockedPeriod_IsRejected()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        // Lock a range that includes today (clock-in uses UTC "today").
        await mediator.Send(new LockPeriodCommand(Today.AddDays(-2), Today.AddDays(2)));

        var clockIn = await mediator.Send(ClockIn());

        clockIn.IsSuccess.Should().BeFalse();
        clockIn.StatusCode.Should().Be(409);
        clockIn.ErrorCode.Should().Be("payroll_period_locked");
    }

    [Fact]
    public async Task ClockIn_AfterUnlock_IsAllowed()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var locked = await mediator.Send(new LockPeriodCommand(Today.AddDays(-2), Today.AddDays(2)));
        await mediator.Send(new UnlockPeriodCommand(locked.Value!.Id));

        var clockIn = await mediator.Send(ClockIn());

        clockIn.IsSuccess.Should().BeTrue();
        clockIn.Value!.ClockIn.Should().NotBe(default);
    }

    // ── Payroll data pull (FR-1 / FR-2) ─────────────────────────────────────

    [Fact]
    public async Task GetPayrollData_ReturnsRowPerEmployee()
    {
        // Give empA a REAL tracked day so it is a genuine (has-data) employee — the row must not depend
        // on the phantom full-month-absent fabrication that ISSUE-090 removes (2026-04-06 is a Monday).
        SeedLog(_tenantA, _empA, new DateOnly(2026, 4, 6));

        var (mediator, _) = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(new GetPayrollDataQuery(2026, 4, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Period.Should().Be("2026-04");
        result.Value.Rows.Should().Contain(r => r.EmployeeId == _empA);
    }

    [Fact]
    public async Task PayrollData_EmployeeWithNoRecords_NotFullyAbsent_ISSUE090()
    {
        // ISSUE-090: payroll-data must NOT fabricate a full-month-absent row for an employee who has NO
        // attendance records for the period. Seed two Tenant-A employees in the SAME period: empA has one
        // real tracked day (present), _empNoRecords has none. April 2026 (fully past) has 22 Mon–Fri
        // working days, so the pre-fix on-demand generation defaults every one of those to ABSENT+LOP for
        // the no-records employee.
        var empNoRecords = Guid.NewGuid();
        using (var db = Db(_tenantA))
        {
            db.Employees.Add(Emp(empNoRecords, _tenantA, Guid.NewGuid(), "A-NO-REC"));
            db.SaveChanges();
        }
        SeedLog(_tenantA, _empA, new DateOnly(2026, 4, 6));   // empA: real data (present)

        int workingDays = 0;
        for (var d = new DateOnly(2026, 4, 1); d <= new DateOnly(2026, 4, 30); d = d.AddDays(1))
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                workingDays++;
        workingDays.Should().Be(22);   // guard the scenario really exercises a "full month" of absences

        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var result = await mediator.Send(new GetPayrollDataQuery(2026, 4, null));

        result.IsSuccess.Should().BeTrue();

        // The no-records employee must NOT be reported as absent/LOP for the whole period. Robust to the
        // fix's chosen representation: either OMITTED (no row) or a no-data marker (row present but not
        // full-month absent). Pre-fix a fabricated row with AbsentDays == LopDays == 22 exists → fails.
        var noRec = result.Value!.Rows.Where(r => r.EmployeeId == empNoRecords).ToList();
        noRec.Should().NotContain(r => r.TotalAbsentDays >= workingDays,
            "an employee with zero attendance records must not be fabricated as absent for the whole period");
        noRec.Should().NotContain(r => r.LopDays >= workingDays);

        // Prove the fix did not simply empty the feed: the employee WITH real data is still reported and
        // is not itself fabricated as full-month absent.
        var withRec = result.Value.Rows.Single(r => r.EmployeeId == _empA);
        withRec.TotalPresentDays.Should().BeGreaterThan(0m);
        withRec.TotalAbsentDays.Should().BeLessThan(workingDays);
    }

    // ── BUG-125: batched shift/working-days resolution (values must be unchanged) ───────────

    [Fact]
    public async Task PayrollData_WorkingDays_BatchResolution_AssignedAndDefaultShift_BUG125()
    {
        // BUG-125: shift/working-days resolution was hoisted OUT of the per-employee loop into a batch
        // (fixed query count regardless of employee count). The per-employee TotalWorkingDays VALUES must
        // be identical to the former per-employee resolution: an assigned (non-default) shift, a
        // default-shift fallback, and their mix all resolve exactly as before.
        var sixDayShift = Guid.NewGuid();
        var empAssigned = Guid.NewGuid();   // Mon–Sat via an explicit effective-dated assignment
        var empDefault = Guid.NewGuid();    // Mon–Fri via the default-shift fallback (no assignment)

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
            db.Employees.AddRange(
                Emp(empAssigned, _tenantA, Guid.NewGuid(), "A-ASSIGNED"),
                Emp(empDefault, _tenantA, Guid.NewGuid(), "A-DEFAULT"));
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA,
                EmployeeId = empAssigned, ShiftId = sixDayShift,
                EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = null,
            });
            db.SaveChanges();
        }

        // Real tracked data so ISSUE-090 does not omit either employee (2026-04-06 is a Monday).
        SeedLog(_tenantA, empAssigned, new DateOnly(2026, 4, 6));
        SeedLog(_tenantA, empDefault, new DateOnly(2026, 4, 6));

        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var result = await mediator.Send(new GetPayrollDataQuery(2026, 4, null));
        result.IsSuccess.Should().BeTrue();

        // Independent oracle: count the shift's working weekdays across April 2026 (the effectiveEnd for an
        // active employee is the month end), matching the former per-employee calendar iteration exactly.
        int Expected(HashSet<int> workingWeekdays)
        {
            int c = 0;
            for (var d = new DateOnly(2026, 4, 1); d <= new DateOnly(2026, 4, 30); d = d.AddDays(1))
            {
                int iso = (int)d.DayOfWeek == 0 ? 7 : (int)d.DayOfWeek;
                if (workingWeekdays.Count == 0 || workingWeekdays.Contains(iso)) c++;
            }
            return c;
        }
        var monFri = new HashSet<int> { 1, 2, 3, 4, 5 };
        var monSat = new HashSet<int> { 1, 2, 3, 4, 5, 6 };

        var assignedRow = result.Value!.Rows.Single(r => r.EmployeeId == empAssigned);
        var defaultRow = result.Value.Rows.Single(r => r.EmployeeId == empDefault);

        assignedRow.TotalWorkingDays.Should().Be(Expected(monSat));
        defaultRow.TotalWorkingDays.Should().Be(Expected(monFri));
        // The six-day shift must schedule strictly more working days than the five-day default — proves the
        // per-employee shift selection survived the hoist (not a single shared value for all employees).
        assignedRow.TotalWorkingDays.Should().BeGreaterThan(defaultRow.TotalWorkingDays);
    }

    // ── Multi-tenant isolation (NFR-3) ──────────────────────────────────────

    [Fact]
    public async Task PeriodLock_IsTenantIsolated()
    {
        var m = Today;
        var (mediatorA, _) = BuildPipeline(_tenantA, _userA);
        await mediatorA.Send(new LockPeriodCommand(
            new DateOnly(m.Year, m.Month, 1), new DateOnly(m.Year, m.Month, 28)));

        // Tenant B must not see Tenant A's lock.
        var (mediatorB, _) = BuildPipeline(_tenantB, _userB);
        var statusB = await mediatorB.Send(new GetPeriodLockQuery(m.Year, m.Month));

        statusB.IsSuccess.Should().BeTrue();
        statusB.Value.Should().BeNull();
    }
}
