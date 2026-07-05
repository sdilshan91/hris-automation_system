// ============================================================================
// Regression suite for the fix/attendance-calc-cluster four-finding cluster. Each test drives the
// REAL Infrastructure services (AttendanceService / OvertimeService / LateEarlyService /
// AttendanceSummaryService) through the EF Core InMemory provider — the calc/authorization logic under
// test is provider-agnostic (no DB race), so InMemory is appropriate and mirrors the existing
// AttendanceServiceTests / OvertimeIntegrationTests / LateEarlyServiceTests harnesses.
//
// Each test is written to FAIL against the pre-fix code (verified via `git show HEAD:` on each service)
// and PASS once the parallel fix lands:
//
//   @TC-ATT-006  · ISSUE-065 (US-ATT-001) — clock-in late flag must use the TENANT timezone, not the
//                  UTC wall-clock. Test: ClockIn_LateFlag_UsesTenantTimezone_ISSUE065.
//   @TC-ATT-067  · ISSUE-078 (US-ATT-006) — the clock-out attendance_log overtime and the overtime_record
//                  (payroll surface) must reconcile on ONE canonical standard, not 480 vs 420. Test:
//                  Overtime_DetectAndReport_SameStandard_ISSUE078.
//   @TC-ATT-112  · ISSUE-084 (US-ATT-008) — the late/early REPORT, the self SCORE and the monthly SUMMARY
//                  must agree on ONE aggregation grain. Test: LateEarly_ReportAndScore_Agree_ISSUE084.
//   @TC-ATT-077  · BUG-049  (US-ATT-006) — an approver holding Attendance.Approve.Team/.All must be able to
//                  reach the overtime decision even with NO linked employee record (not a 403). Test:
//                  OvertimeApprove_HrWithoutEmployeeRecord_NotForbidden_BUG049.
//
// The ISSUE-078 / ISSUE-084 assertions key on CONSISTENCY between surfaces (a robust invariant regardless
// of which canonical value/grain the fix picks). ISSUE-065 asserts the tenant-tz frame; BUG-049 asserts
// the authorization gate. These are behavioural invariants, not tautologies — the seeded data makes each
// pre-fix arm diverge/deny and each post-fix arm reconcile/permit.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AttendanceCalcClusterRegressionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;

    public AttendanceCalcClusterRegressionTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db(string dbName) => TestDbContextFactory.Create(_tenantContext, dbName);

    private ICurrentUser CurrentUser(Guid userId, params string[] permissions)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.UserId.Returns(userId);
        cu.IsAuthenticated.Returns(true);
        cu.Permissions.Returns(permissions);
        return cu;
    }

    private Employee NewEmployee(
        Guid userId, string first, string last, Guid? reportsTo = null,
        EmployeeStatus status = EmployeeStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        UserId = userId,
        EmployeeNo = $"EMP-{Guid.NewGuid().ToString()[..4]}",
        FirstName = first,
        LastName = last,
        Email = $"{first}.{last}@test.com".ToLowerInvariant(),
        Gender = Gender.Male,
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = status,
        IsActive = true,
        ReportsToEmployeeId = reportsTo,
    };

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-065 (US-ATT-001, @TC-ATT-006): clock-in late flag must use the tenant timezone.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClockIn_LateFlag_UsesTenantTimezone_ISSUE065()
    {
        // A punch that is ON TIME in the tenant-local frame but LATE in the raw UTC frame.
        //
        // ClockInAsync stamps ClockIn = DateTime.UtcNow (the instant is not client-supplied), so the
        // scenario is anchored to the current instant: the shift starts at the tenant-LOCAL current
        // time-of-day, so clocking in "now" is on time locally. A behind-UTC timezone makes the same
        // instant's UTC time-of-day appear hours past that (local) shift start.
        //
        // The offset (whole hours) is chosen < the current UTC hour so 'local now' stays on the same UTC
        // calendar day — LateEarlyCalculator does a naive same-day comparison, so a midnight wrap would
        // confound the PRE-fix arm. (Between 00:00–01:00 UTC no such offset exists; the post-fix
        // assertion still holds, only the pre-fix demonstration is weaker in that ~1h/day window.)
        var nowUtc = DateTime.UtcNow;
        var offsetHours = Math.Clamp(nowUtc.Hour - 1, 1, 12);
        var tzId = $"Etc/GMT+{offsetHours}";        // POSIX sign is inverted: Etc/GMT+5 == UTC-05:00, no DST.
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var shiftStartLocal = TimeOnly.FromDateTime(localNow);   // on time locally right now.

        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();

        using (var db = Db(dbName))
        {
            // The fix reads the tenant timezone from the DB (ITenantContext carries no timezone).
            db.Tenants.Add(new Tenant
            {
                Id = _tenantId,
                Subdomain = "tzco",
                Name = "TZ Co",
                Status = TenantStatus.Active,
                TimeZone = tzId,
            });

            var emp = NewEmployee(userId, "Tina", "Zone");
            db.Employees.Add(emp);

            var shift = new Shift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                Name = "Local Day Shift",
                Type = ShiftType.Single,
                StartTime = shiftStartLocal,
                EndTime = shiftStartLocal.AddHours(8),
                BreakDurationMinutes = 60,
                GracePeriodMinutes = 5,          // small, unambiguous grace.
                WorkingDays = new List<int> { 1, 2, 3, 4, 5, 6, 7 },
                IsDefault = true,
                IsActive = true,
            };
            db.Shifts.Add(shift);
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = emp.Id,
                ShiftId = shift.Id,
                EffectiveFrom = DateOnly.FromDateTime(nowUtc).AddDays(-30),
            });
            db.SaveChanges();
        }

        var result = await BuildAttendanceService(dbName, CurrentUser(userId))
            .ClockInAsync(new ClockInData { Source = "WEB", IpAddress = "10.0.0.1", UserAgent = "Chrome" });

        result.IsSuccess.Should().BeTrue();

        // POST-fix invariant: on-time-in-tenant-local ⇒ NOT flagged late. PRE-fix compares the UTC
        // time-of-day (offsetHours past the local shift start) and wrongly flags it late by offsetHours*60.
        result.Value!.IsLate.Should().BeFalse(
            "a punch on time in the tenant timezone must not be flagged late from its UTC wall-clock");
        result.Value.LateMinutes.Should().BeLessThan(offsetHours * 60);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-078 (US-ATT-006, @TC-ATT-067): clock-out log overtime and the overtime_record must reconcile.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Overtime_DetectAndReport_SameStandard_ISSUE078()
    {
        // Same clock-out, two overtime surfaces. The attendance_log overtime (US-ATT-002 path) uses the
        // tenant StandardWorkMinutes (480); the overtime_record (US-ATT-006 payroll surface) uses the
        // shift-derived standard (09:00–17:00 less 60 break = 420). For a net-540 day the two diverge
        // pre-fix (log 60 vs record 120). The fix converges both on ONE canonical standard.
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        using (var db = Db(dbName))
        {
            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                StandardWorkMinutes = 480,
                OvertimeThresholdMinutes = 0,           // log OT = net - 480 = 60.
                AutoBreakMinutes = 0,                   // net == gross for a predictable span.
                AutoBreakThresholdMinutes = 100_000,
                OvertimeMinimumThresholdMinutes = 30,   // record OT threshold.
                MaxDailyOvertimeMinutes = 240,
                MaxWeeklyOvertimeMinutes = 100_000,
            });

            var emp = NewEmployee(userId, "Otto", "Time");
            db.Employees.Add(emp);

            // "Day Shift" 09:00–17:00 break 60 → 420 net standard (≠ StandardWorkMinutes 480).
            var shift = new Shift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                Name = "Day Shift",
                Type = ShiftType.Single,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(17, 0),
                BreakDurationMinutes = 60,
                GracePeriodMinutes = 15,
                WorkingDays = new List<int> { 1, 2, 3, 4, 5, 6, 7 },
                IsDefault = true,
                IsActive = true,
            };
            db.Shifts.Add(shift);
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = emp.Id,
                ShiftId = shift.Id,
                EffectiveFrom = DateOnly.FromDateTime(nowUtc).AddDays(-30),
            });

            // Open clock-in 540 minutes ago → net worked = 540 (no auto-break).
            db.AttendanceLogs.Add(new AttendanceLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = emp.Id,
                ClockIn = nowUtc.AddMinutes(-540),
                Source = "WEB",
            });
            db.SaveChanges();
        }

        var clockOut = await BuildAttendanceService(dbName, CurrentUser(userId))
            .ClockOutAsync(new ClockOutData { IpAddress = "10.0.0.1" });

        clockOut.IsSuccess.Should().BeTrue();
        var logOvertime = clockOut.Value!.OvertimeMinutes;   // attendance_log surface.

        OvertimeRecord record;
        using (var db = Db(dbName))
            record = db.OvertimeRecords.Single(o => o.OvertimeMinutes >= 0);

        logOvertime.Should().BeGreaterThan(0);
        record.OvertimeMinutes.Should().BeGreaterThan(0);

        // The two surfaces produced in the SAME clock-out transaction must agree on one standard.
        logOvertime.Should().Be(record.OvertimeMinutes,
            "the clock-out attendance_log overtime and the overtime_record must use one canonical standard");

        // The monthly HR overtime report reads the record surface — it too must reconcile with the log.
        var report = await BuildOvertimeService(dbName, CurrentUser(userId))
            .GetMonthlyReportAsync(record.Date.Year, record.Date.Month);
        report.IsSuccess.Should().BeTrue();
        var reportRow = report.Value!.Items.Single(r => r.EmployeeId == record.EmployeeId);
        reportRow.PendingMinutes.Should().Be(logOvertime,
            "the monthly overtime report must reconcile with the clock-out log's overtime");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-084 (US-ATT-008, @TC-ATT-112): report + score + summary must agree on one grain.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LateEarly_ReportAndScore_Agree_ISSUE084()
    {
        // TWO attendance_log rows on the SAME date, both late + early departure. The report and self-score
        // count raw late LOG ROWS (per-punch → 2); the monthly summary collapses to one log/day (per-day
        // → 1). For the same employee/month the three US-ATT-008 read surfaces therefore disagree pre-fix.
        // The fix makes them share ONE aggregation grain — asserted here as equality across all three.
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var lateDate = new DateOnly(2026, 5, 4);   // a fully-elapsed month (deterministic bounds).

        Guid empId;
        using (var db = Db(dbName))
        {
            var emp = NewEmployee(userId, "Lena", "Early");
            empId = emp.Id;
            db.Employees.Add(emp);

            // Representative log (most worked minutes) and a second same-day log — both late + early.
            db.AttendanceLogs.Add(SameDayLog(emp.Id, lateDate, workMinutes: 480));
            db.AttendanceLogs.Add(SameDayLog(emp.Id, lateDate, workMinutes: 300));
            db.SaveChanges();
        }

        var acting = CurrentUser(userId, PermissionCatalog.Attendance.ViewAll);

        var report = await BuildLateEarlyService(dbName, acting)
            .GetReportAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), null, empId, "all");
        report.IsSuccess.Should().BeTrue();
        var reportRow = report.Value!.Rows.Single(r => r.EmployeeId == empId);

        var score = await BuildLateEarlyService(dbName, acting).GetMyScoreAsync(2026, 5);
        score.IsSuccess.Should().BeTrue();

        var summary = await BuildSummaryService(dbName, acting).GetMonthlyAsync(2026, 5, new MonthlySummaryFilter());
        summary.IsSuccess.Should().BeTrue();
        var summaryRow = summary.Value!.Rows.Single(r => r.EmployeeId == empId);

        // All three surfaces must report the same late count and the same early-departure count for the
        // same employee/month. Pre-fix the per-punch report/score (2) diverge from the per-day summary (1).
        reportRow.LateCount.Should().Be(summaryRow.LateCount,
            "the late/early report and the monthly summary must use one aggregation grain");
        score.Value!.LateCount.Should().Be(summaryRow.LateCount,
            "the self-score and the monthly summary must use one aggregation grain");
        reportRow.EarlyDepartureCount.Should().Be(summaryRow.EarlyDepartureCount);
        score.Value.EarlyDepartureCount.Should().Be(summaryRow.EarlyDepartureCount);
    }

    private AttendanceLog SameDayLog(Guid employeeId, DateOnly date, int workMinutes) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantId,
        EmployeeId = employeeId,
        ClockIn = date.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Utc),
        ClockOut = date.ToDateTime(new TimeOnly(16, 30), DateTimeKind.Utc),
        TotalWorkMinutes = workMinutes,
        Status = "COMPLETE",
        Source = "WEB",
        IsLate = true,
        LateMinutes = 30,
        IsEarlyDeparture = true,
        EarlyDepartureMinutes = 30,
    };

    // ════════════════════════════════════════════════════════════════════════
    //  BUG-049 (US-ATT-006, @TC-ATT-077): an approver with Attendance.Approve.Team and NO employee
    //  record must reach the overtime decision (not a 403).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OvertimeApprove_HrWithoutEmployeeRecord_NotForbidden_BUG049()
    {
        // The approver holds Attendance.Approve.Team but has NO linked employee record (an HR persona).
        // Pre-fix, LoadForDecisionAsync resolves the actor via Employees.FirstOrDefault(UserId==...) and
        // returns 403 "No employee record is linked" — so HR can never action overtime. The fix permits a
        // permission-holding approver to reach the decision.
        var dbName = Guid.NewGuid().ToString();
        Guid overtimeId;

        using (var db = Db(dbName))
        {
            var manager = NewEmployee(Guid.NewGuid(), "Mona", "Manager");
            var report = NewEmployee(Guid.NewGuid(), "Rex", "Report", reportsTo: manager.Id);
            db.Employees.AddRange(manager, report);

            overtimeId = BaseEntity.NewUuidV7();
            db.OvertimeRecords.Add(new OvertimeRecord
            {
                Id = overtimeId,
                TenantId = _tenantId,
                EmployeeId = report.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                OvertimeMinutes = 90,
                ApprovedMinutes = null,
                Multiplier = 1.5m,
                Type = OvertimeType.AutoDetected,
                Status = OvertimeStatus.Pending,
                Reason = "Auto-detected overtime on clock-out.",
                IsPayrollReady = false,
            });
            db.SaveChanges();
        }

        // HR actor: a user id with no matching Employees row, but holding the approve permission.
        var hr = CurrentUser(Guid.NewGuid(), PermissionCatalog.Attendance.ApproveTeam);

        var result = await BuildOvertimeService(dbName, hr).ApproveAsync(overtimeId, null, "Approved by HR.");

        // Pre-fix returns 403 ("No employee record is linked to the current user."). Post-fix the
        // permission-holding approver reaches the decision — assert it is NOT forbidden.
        result.StatusCode.Should().NotBe(403,
            "an approver holding Attendance.Approve.Team must reach the overtime decision even without an employee record");
    }

    // ── service builders (fresh context per call on the shared InMemory db) ──────────────────────────

    private AttendanceService BuildAttendanceService(string dbName, ICurrentUser currentUser)
    {
        var db = Db(dbName);
        var overtime = new OvertimeService(
            db, _tenantContext, currentUser, Substitute.For<ILogger<OvertimeService>>());
        var shift = new ShiftService(
            db, _tenantContext, currentUser, Substitute.For<ILogger<ShiftService>>());
        return new AttendanceService(
            db, _tenantContext, currentUser, overtime, shift, Substitute.For<ILogger<AttendanceService>>());
    }

    private OvertimeService BuildOvertimeService(string dbName, ICurrentUser currentUser)
        => new(Db(dbName), _tenantContext, currentUser, Substitute.For<ILogger<OvertimeService>>());

    private LateEarlyService BuildLateEarlyService(string dbName, ICurrentUser currentUser)
        => new(Db(dbName), _tenantContext, currentUser, Substitute.For<ILogger<LateEarlyService>>());

    private AttendanceSummaryService BuildSummaryService(string dbName, ICurrentUser currentUser)
        => new(Db(dbName), _tenantContext, Substitute.For<IReportExportStorage>(),
               Substitute.For<ILogger<AttendanceSummaryService>>());
}
