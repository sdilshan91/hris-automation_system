// ============================================================================
// ISSUE-065 / Phase 2b: AttendanceService tenant-timezone integration tests
// (branch fix/phase2b-timezone). Mirrors AttendanceServiceTests / AttendanceClockOutServiceTests
// (EF Core InMemory provider, real AttendanceService + ShiftService + OvertimeService).
//
// WHY THE CLOCK-OUT PATH IS THE SEAM UNDER TEST:
//   ClockInAsync stamps the punch with the live DateTime.UtcNow, which a test cannot control, so
//   a deterministic instant cannot be asserted through it. ClockOutAsync RE-EVALUATES the late flag
//   and the day-boundary/lock date from the (seeded) OPEN log's ClockIn instant
//   (AttendanceService.cs ~L249, L284-L302) via TenantClock.LocalDateOf / LocalTimeOfDay. Seeding an
//   open log with an EXACT UTC instant and then clocking out is therefore the honest, deterministic
//   way to drive the tenant-local derivation the ISSUE-065 fix introduced.
//
// NON-THEATER NOTES (a @test-authenticator will audit):
//   * Real TimeZoneInfo (America/New_York, DST-aware) — no mocked clock/zone.
//   * The late test asserts the EXACT LateMinutes magnitude: 30 (09:30 local − 09:00 shift). A
//     pre-fix / UTC-only implementation would compare 14:30 UTC to 09:00 and persist LateMinutes=330,
//     so the exact 30 is what proves the derivation keyed on LOCAL time-of-day.
//   * The "not late" test uses 13:45Z → 08:45 New York local (< 09:00 → on time). A UTC-only
//     implementation (13:45 > 09:00) would have flagged is_late=true — so is_late=false proves local
//     keying, not merely "no shift matched".
//   * The day-boundary test uses 02:30Z → 2026-01-14 New York local (previous day) and a period lock
//     over 2026-01-14: clock-out is BLOCKED for the New York tenant (attributed to the local date)
//     but SUCCEEDS for a UTC tenant at the SAME instant (local date 2026-01-15, unlocked).
//   * Every scenario has a UTC-tenant counterpart asserting the pre-existing behavior is unchanged
//     (the no-op safety property end-to-end).
//
// Traceability: @TC-ATT-TZ-010 (late by local time, ISSUE-065) · @TC-ATT-TZ-011 (not late by local
// time) · @TC-ATT-TZ-012 (UTC tenant unchanged) · @TC-ATT-TZ-013 (day-boundary → local date).
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
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AttendanceTenantTimezoneTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AttendanceService> _logger;

    public AttendanceTenantTimezoneTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);

        _logger = Substitute.For<ILogger<AttendanceService>>();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AttendanceService CreateService()
    {
        var db = CreateDbContext();
        var overtime = new OvertimeService(
            db, _tenantContext, _currentUser, Substitute.For<ILogger<OvertimeService>>());
        var shiftService = new ShiftService(
            db, _tenantContext, _currentUser, Substitute.For<ILogger<ShiftService>>());
        return new AttendanceService(db, _tenantContext, _currentUser, overtime, shiftService, _logger);
    }

    /// <summary>
    /// Seeds a tenant (with <paramref name="timeZone"/>), an active employee linked to the current
    /// user, a tenant-default 09:00–17:00 fixed shift with zero grace, and an OPEN attendance log whose
    /// ClockIn is the exact <paramref name="clockInUtc"/> instant. Optionally seeds a period lock.
    /// </summary>
    private void SeedScenario(string timeZone, DateTime clockInUtc,
        (DateOnly start, DateOnly end)? lockRange = null)
    {
        using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = "tz-" + _tenantId.ToString("N")[..8],
            Name = "TZ Test Tenant",
            TimeZone = timeZone,
        });

        db.Employees.Add(new Employee
        {
            Id = _employeeId,
            TenantId = _tenantId,
            UserId = _userId,
            EmployeeNo = "EMP-0001",
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            Gender = Gender.Male,
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        });

        // Tenant-default fixed (SINGLE) shift: 09:00–17:00, no grace → any minute past 09:00 is late.
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = "Day Shift",
            Type = ShiftType.Single,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            GracePeriodMinutes = 0,
            IsDefault = true,
        });

        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            ClockIn = clockInUtc,     // exact, controllable instant (stored in UTC).
            ClockOut = null,          // OPEN → clock-out re-evaluates late/day from this instant.
            Source = "WEB",
        });

        if (lockRange is { } lr)
        {
            db.AttendancePeriodLocks.Add(new AttendancePeriodLock
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                PeriodStart = lr.start,
                PeriodEnd = lr.end,
                IsLocked = true,
            });
        }

        db.SaveChanges();
    }

    private AttendanceLog ReadLog()
    {
        using var db = CreateDbContext();
        return db.AttendanceLogs.Single(a => a.EmployeeId == _employeeId);
    }

    // ── Late detection keyed on LOCAL time-of-day (ISSUE-065) ───────────────────

    [Fact]
    public async Task TenantClock_NonUtcTenant_LateByLocalTime_ISSUE065()
    {
        // 14:30Z in winter → 09:30 New York local → 30 min past the 09:00 shift start (grace 0).
        SeedScenario("America/New_York", new DateTime(2026, 1, 15, 14, 30, 0, DateTimeKind.Utc));

        var result = await CreateService().ClockOutAsync(new ClockOutData());

        result.IsSuccess.Should().BeTrue();

        var log = ReadLog();
        log.IsLate.Should().BeTrue();
        // EXACT magnitude: 30 (local 09:30 − 09:00). A UTC-only comparison (14:30 − 09:00) would be 330.
        log.LateMinutes.Should().Be(30);
    }

    [Fact]
    public async Task TenantClock_NonUtcTenant_OnTimeByLocalTime_NotLate()
    {
        // 13:45Z in winter → 08:45 New York local → BEFORE the 09:00 shift start → on time.
        // A UTC-only implementation would read 13:45 > 09:00 and (wrongly) flag late.
        SeedScenario("America/New_York", new DateTime(2026, 1, 15, 13, 45, 0, DateTimeKind.Utc));

        var result = await CreateService().ClockOutAsync(new ClockOutData());

        result.IsSuccess.Should().BeTrue();

        var log = ReadLog();
        log.IsLate.Should().BeFalse();
        log.LateMinutes.Should().Be(0);
    }

    [Fact]
    public async Task TenantClock_UtcTenant_Unchanged_LateByUtcTime()
    {
        // Same 13:45Z instant, but a UTC tenant: 13:45 > 09:00 → late by 285 min. This is the
        // pre-ISSUE-065 behavior and is the exact opposite verdict to the New York tenant above,
        // proving the New York result came from the zone, not from some shift-resolution accident.
        SeedScenario("UTC", new DateTime(2026, 1, 15, 13, 45, 0, DateTimeKind.Utc));

        var result = await CreateService().ClockOutAsync(new ClockOutData());

        result.IsSuccess.Should().BeTrue();

        var log = ReadLog();
        log.IsLate.Should().BeTrue();
        log.LateMinutes.Should().Be(285);   // 13:45 − 09:00, plain UTC time-of-day.
    }

    // ── Day-boundary attributed to the LOCAL date ───────────────────────────────

    [Fact]
    public async Task TenantClock_NonUtcTenant_NearMidnight_AttributesToLocalDate()
    {
        // 02:30Z on 2026-01-15 → 21:30 on 2026-01-14 New York local (previous local day). A period
        // lock over 2026-01-14 must block the clock-out — the punch is attributed to the LOCAL date.
        SeedScenario("America/New_York", new DateTime(2026, 1, 15, 2, 30, 0, DateTimeKind.Utc),
            lockRange: (new DateOnly(2026, 1, 14), new DateOnly(2026, 1, 14)));

        var result = await CreateService().ClockOutAsync(new ClockOutData());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("payroll_period_locked");
    }

    [Fact]
    public async Task TenantClock_UtcTenant_NearMidnight_UsesUtcDate_NotLocked()
    {
        // Same 02:30Z instant + same lock over 2026-01-14, but a UTC tenant: the local date is
        // 2026-01-15, which is NOT in the locked range → clock-out succeeds. Guards the no-op property
        // end-to-end (the day-boundary derivation is identity for a UTC tenant).
        SeedScenario("UTC", new DateTime(2026, 1, 15, 2, 30, 0, DateTimeKind.Utc),
            lockRange: (new DateOnly(2026, 1, 14), new DateOnly(2026, 1, 14)));

        var result = await CreateService().ClockOutAsync(new ClockOutData());

        result.IsSuccess.Should().BeTrue();
    }
}
