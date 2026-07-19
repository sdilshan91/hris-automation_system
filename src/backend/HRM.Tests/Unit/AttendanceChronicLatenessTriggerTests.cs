// ============================================================================
// US-ATT-008 FR-7 (DF-33 / ISSUE-087) — chronic-lateness escalation CROSSING logic, exercised through the
// real ClockInAsync path (AttendanceService + real LateEarlyService + real ShiftService/OvertimeService,
// EF Core InMemory). The notification service is an NSubstitute so we can assert exactly when the escalation
// fires.
//
// The crossing condition implemented in AttendanceService is:
//   fire  ⇔  log.IsLate  ∧  ChronicThreshold > 0
//            ∧  monthToDate distinct late days == ChronicThreshold + 1   (the exact crossing)
//            ∧  this punch is the FIRST late log of that local day       (no same-day re-fire)
//
// DETERMINISM: ClockInAsync stamps DateTime.UtcNow (a test cannot control it), so the punch's day is "today".
// We use a UTC tenant (local date == UTC date) and a 00:00 tenant-default shift with zero grace, so any real
// clock-in today is late. The prior distinct late days are seeded on OTHER day-numbers of the CURRENT month
// (1..28, excluding today) so the total distinct-day count after the punch is deterministic regardless of the
// calendar day the suite runs on. What is under test is the count + same-day-guard fire logic, not the wall clock.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AttendanceChronicLatenessTriggerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AttendanceService> _logger = NullLogger<AttendanceService>.Instance;
    private readonly IAttendanceNotificationService _notifications =
        Substitute.For<IAttendanceNotificationService>();

    // "Today" in the UTC tenant — the day the real punch lands on.
    private static readonly DateTime NowUtc = DateTime.UtcNow;
    private static readonly DateOnly TodayUtc = DateOnly.FromDateTime(NowUtc);

    public AttendanceChronicLatenessTriggerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AttendanceService CreateService()
    {
        var db = CreateDbContext();
        var overtime = new OvertimeService(
            db, _tenantContext, _currentUser, Substitute.For<ILogger<OvertimeService>>());
        var shift = new ShiftService(
            db, _tenantContext, _currentUser, Substitute.For<ILogger<ShiftService>>());
        var lateEarly = new LateEarlyService(
            db, _tenantContext, _currentUser, Substitute.For<ILogger<LateEarlyService>>());
        return new AttendanceService(
            db, _tenantContext, _currentUser, overtime, shift, _logger,
            workflowRuntime: null, notifications: _notifications, lateEarly: lateEarly);
    }

    /// <summary>
    /// Seeds an active employee linked to the current user, a 00:00–23:59 tenant-default fixed shift (zero grace →
    /// every real clock-in today is late), a late policy with <paramref name="chronicThreshold"/>, and
    /// <paramref name="priorLateDays"/> distinct prior late days in the current month (on day-numbers other than
    /// today), each a CLOSED late log so they do not trip the open-record check.
    /// </summary>
    private void Seed(int chronicThreshold, int priorLateDays)
    {
        using var db = CreateDbContext();

        db.Employees.Add(new Employee
        {
            Id = _employeeId, TenantId = _tenantId, UserId = _userId,
            EmployeeNo = "EMP-0001", FirstName = "John", LastName = "Doe", Email = "john@test.com",
            Gender = Gender.Male, DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "All-day",
            Type = ShiftType.Single, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(23, 59),
            GracePeriodMinutes = 0, IsDefault = true,
        });

        db.LatePolicies.Add(new LatePolicy
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId,
            ChronicThreshold = chronicThreshold, IsActive = true,
        });

        foreach (var day in PickDays(priorLateDays, TodayUtc.Day))
        {
            var clockIn = new DateTime(TodayUtc.Year, TodayUtc.Month, day, 10, 0, 0, DateTimeKind.Utc);
            db.AttendanceLogs.Add(new AttendanceLog
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId,
                ClockIn = clockIn, ClockOut = clockIn.AddHours(8),
                IsLate = true, LateMinutes = 30, Source = "WEB",
            });
        }

        db.SaveChanges();
    }

    /// <summary>N distinct day-numbers in 1..28 (every month has these) excluding <paramref name="excludeDay"/>.</summary>
    private static List<int> PickDays(int count, int excludeDay)
    {
        var days = new List<int>();
        for (var d = 1; d <= 28 && days.Count < count; d++)
            if (d != excludeDay) days.Add(d);
        return days;
    }

    private static ClockInData Data() => new() { Source = "WEB", IpAddress = "10.0.0.1", UserAgent = "Chrome/120" };

    private async Task AssertFiredOnce(int expectedLateCount, int expectedThreshold) =>
        await _notifications.Received(1).NotifyChronicLatenessAsync(
            _employeeId, expectedLateCount, expectedThreshold, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());

    private async Task AssertNeverFired() =>
        await _notifications.DidNotReceive().NotifyChronicLatenessAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());

    // (i) below threshold → does NOT fire (4 prior + today = 5 == threshold, not threshold+1) ─────────
    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task ClockIn_BelowThreshold_DoesNotEscalate()
    {
        Seed(chronicThreshold: 5, priorLateDays: 4);

        var result = await CreateService().ClockInAsync(Data());

        result.IsSuccess.Should().BeTrue();
        await AssertNeverFired();
    }

    // (ii) exact crossing → fires exactly once (5 prior + today = 6 == threshold+1) ────────────────────
    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task ClockIn_AtCrossing_EscalatesExactlyOnce()
    {
        Seed(chronicThreshold: 5, priorLateDays: 5);

        var result = await CreateService().ClockInAsync(Data());

        result.IsSuccess.Should().BeTrue();
        await AssertFiredOnce(expectedLateCount: 6, expectedThreshold: 5);
    }

    // (iii) a SECOND late punch the same day → does NOT re-fire (distinct-day count unchanged) ──────────
    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task ClockIn_SecondLatePunchSameDay_DoesNotReEscalate()
    {
        Seed(chronicThreshold: 5, priorLateDays: 5);

        // First punch crosses the threshold and fires once.
        (await CreateService().ClockInAsync(Data())).IsSuccess.Should().BeTrue();
        // Close it so a second clock-in is allowed.
        (await CreateService().ClockOutAsync(new ClockOutData())).IsSuccess.Should().BeTrue();
        // Second late punch, same local day → count still 6, but not the first late log of the day.
        (await CreateService().ClockInAsync(Data())).IsSuccess.Should().BeTrue();

        await AssertFiredOnce(expectedLateCount: 6, expectedThreshold: 5);
    }

    // (iv) a later late day where the count is already > threshold+1 → does NOT fire (6 prior + today = 7)
    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task ClockIn_AlreadyAboveCrossing_DoesNotEscalate()
    {
        Seed(chronicThreshold: 5, priorLateDays: 6);

        var result = await CreateService().ClockInAsync(Data());

        result.IsSuccess.Should().BeTrue();
        await AssertNeverFired();
    }

    // (v) chronic threshold disabled (== 0) → never fires. Seed prior:0 so today is the FIRST late day and
    // mtdLateDays == 1 == (0 + 1): the crossing ARITHMETIC is satisfied, so only the `ChronicThreshold > 0`
    // disable-guard suppresses the fire. (Seeding prior:6 would let 7 != 0+1 mask the guard — a guard-removal
    // mutant would then survive.) This arm pins the guard itself.
    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task ClockIn_ChronicThresholdZero_DoesNotEscalate()
    {
        Seed(chronicThreshold: 0, priorLateDays: 0);

        var result = await CreateService().ClockInAsync(Data());

        result.IsSuccess.Should().BeTrue();
        await AssertNeverFired();
    }
}
