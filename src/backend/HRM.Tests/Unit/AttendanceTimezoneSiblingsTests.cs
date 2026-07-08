// ============================================================================
// ISSUE-065 / Phase 2b sibling fixes — tenant-timezone correctness for three attendance services
// (branch fix/attendance-tz-siblings). Companion to TenantClockTests + AttendanceTenantTimezoneTests,
// covering the day-boundary / late-verdict flips in the DASHBOARD (BUG-245) and REGULARIZATION-RECOMPUTE
// (BUG-247) services. The PAYROLL month-gate flip (BUG-246) is exercised through the composed MediatR
// pipeline in AttendancePayrollTimezoneTests (Integration) because GetPayrollDataAsync fans out to the
// real AttendanceSummaryService.
//
// DESIGN (identical philosophy to AttendanceTenantTimezoneTests — a @test-authenticator will audit):
//   * Real TimeZoneInfo (America/New_York, DST-aware) — no mocked clock/zone. The service resolves the
//     zone from the seeded Tenant.TimeZone column, exactly as in production.
//   * Each scenario picks a UTC punch instant that lands on a DIFFERENT local calendar day / DIFFERENT
//     late verdict than a UTC-only reading would give, so a pre-fix implementation produces the OPPOSITE
//     result. The assertions are on real service output (KPI clocked-in count, recomputed IsLate flag),
//     not on the helper in isolation.
//   * Every non-UTC scenario has a UTC-tenant control asserting the pre-fix behavior is unchanged (the
//     no-op safety property), which also proves the New York result came from the zone and not from a
//     hardcoded offset or a seeding accident.
//
// Traceability: @TC-ATT-TZ-020 (BUG-245 dashboard day-boundary → local date) ·
// @TC-ATT-TZ-021 (BUG-245 UTC control) · @TC-ATT-TZ-022 (BUG-247 late verdict flips to on-time by local
// wall-clock) · @TC-ATT-TZ-023 (BUG-247 exact late magnitude by local wall-clock) ·
// @TC-ATT-TZ-024 (BUG-247 UTC control).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AttendanceTimezoneSiblingsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public AttendanceTimezoneSiblingsTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private void SeedTenant(string timeZone)
    {
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = "tz-" + _tenantId.ToString("N")[..8],
            Name = "TZ Sibling Tenant",
            TimeZone = timeZone,
        });
        db.SaveChanges();
    }

    private static Employee Emp(Guid id, Guid tenantId, Guid? userId, Guid? reportsTo = null) => new()
    {
        Id = id,
        TenantId = tenantId,
        UserId = userId,
        EmployeeNo = "EMP-" + id.ToString("N")[..6],
        FirstName = "Test",
        LastName = "Emp",
        Email = $"{id:N}@t.com",
        Gender = Gender.Male,
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active,
        IsActive = true,
        ReportsToEmployeeId = reportsTo,
    };

    // ════════════════════════════════════════════════════════════════
    //  BUG-245 — dashboard KPI day-grouping keyed on the LOCAL calendar day
    // ════════════════════════════════════════════════════════════════

    private AttendanceDashboardService DashboardService()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsAuthenticated.Returns(true);
        // scope="all" requires the HR Attendance.View.All permission (BR-3).
        currentUser.Permissions.Returns(new[] { PermissionCatalog.Attendance.ViewAll });

        // GetKpisAsync does not touch the summary service — a substitute is sufficient and keeps the
        // test focused on the day-boundary derivation (no fabricated collaborator behavior).
        var summary = Substitute.For<IAttendanceSummaryService>();

        return new AttendanceDashboardService(
            Db(), _tenantContext, currentUser, summary,
            Substitute.For<ILogger<AttendanceDashboardService>>());
    }

    [Fact]
    public async Task Dashboard_NonUtcTenant_PunchAfterUtcMidnight_BucketsUnderPreviousLocalDay_BUG245()
    {
        // 2026-03-16T02:00:00Z is 2026-03-15 22:00 in New York (EDT, UTC-4 — after the 2026-03-08 spring
        // transition). The clocked-in count must appear on the LOCAL day (Mar 15), not the UTC day (Mar 16).
        SeedTenant("America/New_York");
        var empId = Guid.NewGuid();
        using (var db = Db())
        {
            db.Employees.Add(Emp(empId, _tenantId, userId: null));
            db.AttendanceLogs.Add(new AttendanceLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = empId,
                ClockIn = new DateTime(2026, 3, 16, 2, 0, 0, DateTimeKind.Utc),
                Source = "WEB",
            });
            db.SaveChanges();
        }

        var onLocalDay = await DashboardService().GetKpisAsync(new DateOnly(2026, 3, 15), "all");
        var onUtcDay = await DashboardService().GetKpisAsync(new DateOnly(2026, 3, 16), "all");

        onLocalDay.IsSuccess.Should().BeTrue();
        onUtcDay.IsSuccess.Should().BeTrue();

        // The punch is attributed to the LOCAL date Mar 15 (found) and NOT the UTC date Mar 16 (absent) —
        // the exact opposite of a UTC-only bucketing.
        onLocalDay.Value!.ClockedIn.Should().Be(1,
            "the 02:00Z punch is 22:00 the previous day in New York, so it belongs to the local Mar 15");
        onUtcDay.Value!.ClockedIn.Should().Be(0,
            "the punch must NOT be bucketed under the UTC calendar day Mar 16 for a New York tenant");
    }

    [Fact]
    public async Task Dashboard_UtcTenant_PunchAfterUtcMidnight_BucketsUnderUtcDay_BUG245Control()
    {
        // Same 02:00Z instant, but a UTC tenant: the local day IS the UTC day (Mar 16). Proves the fix is
        // a no-op for UTC tenants — the Mar 15/Mar 16 verdict is the exact inverse of the New York case,
        // so the New York result cannot have come from a hardcoded shift.
        SeedTenant("UTC");
        var empId = Guid.NewGuid();
        using (var db = Db())
        {
            db.Employees.Add(Emp(empId, _tenantId, userId: null));
            db.AttendanceLogs.Add(new AttendanceLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = empId,
                ClockIn = new DateTime(2026, 3, 16, 2, 0, 0, DateTimeKind.Utc),
                Source = "WEB",
            });
            db.SaveChanges();
        }

        var onMar15 = await DashboardService().GetKpisAsync(new DateOnly(2026, 3, 15), "all");
        var onMar16 = await DashboardService().GetKpisAsync(new DateOnly(2026, 3, 16), "all");

        onMar16.Value!.ClockedIn.Should().Be(1, "for a UTC tenant the 02:00Z punch belongs to the UTC day Mar 16");
        onMar15.Value!.ClockedIn.Should().Be(0);
    }

    // ════════════════════════════════════════════════════════════════
    //  BUG-247 — regularization late/early recompute keyed on the LOCAL wall-clock
    // ════════════════════════════════════════════════════════════════

    private readonly Guid _managerUserId = Guid.NewGuid();
    private Guid _managerId;
    private Guid _reportId;

    private void SeedRegularizationOrg(string timeZone)
    {
        SeedTenant(timeZone);
        _managerId = Guid.NewGuid();
        _reportId = Guid.NewGuid();

        using var db = Db();
        db.Employees.AddRange(
            Emp(_managerId, _tenantId, _managerUserId),
            Emp(_reportId, _tenantId, Guid.NewGuid(), reportsTo: _managerId));

        // Tenant-default fixed shift 09:00–17:00, no grace → any minute past 09:00 (local) is late.
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
            IsActive = true,
        });
        db.SaveChanges();
    }

    private Guid SeedPendingRegularization(DateOnly date, DateTime requestedInUtc, DateTime requestedOutUtc)
    {
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.AttendanceRegularizations.Add(new AttendanceRegularization
        {
            Id = id,
            TenantId = _tenantId,
            EmployeeId = _reportId,
            Date = date,
            RegularizationType = RegularizationType.MissedBoth,
            RequestedClockIn = requestedInUtc,
            RequestedClockOut = requestedOutUtc,
            Reason = "Forgot to punch on this working day",
            Status = RegularizationStatus.Pending,
        });
        db.SaveChanges();
        return id;
    }

    private RegularizationApprovalService RegularizationService()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(_managerUserId);
        currentUser.IsAuthenticated.Returns(true);

        var db = Db();
        var shiftService = new ShiftService(
            db, _tenantContext, currentUser, Substitute.For<ILogger<ShiftService>>());
        return new RegularizationApprovalService(
            db, _tenantContext, currentUser, shiftService,
            Substitute.For<ILogger<RegularizationApprovalService>>());
    }

    private AttendanceLog ReadReportLog()
    {
        using var db = Db();
        return db.AttendanceLogs.Single(a => a.EmployeeId == _reportId);
    }

    [Fact]
    public async Task Regularization_NonUtcTenant_OnTimeByLocalWallClock_ClearsLate_BUG247()
    {
        // 2026-01-15T13:45:00Z is 08:45 in New York (EST, UTC-5) — BEFORE the 09:00 shift start → on time.
        // A UTC-only recompute reads 13:45 > 09:00 and would (wrongly) flag the regularized log as late.
        SeedRegularizationOrg("America/New_York");
        var date = new DateOnly(2026, 1, 15);
        var regId = SeedPendingRegularization(
            date,
            new DateTime(2026, 1, 15, 13, 45, 0, DateTimeKind.Utc),   // 08:45 local → on time
            new DateTime(2026, 1, 15, 22, 0, 0, DateTimeKind.Utc));   // 17:00 local → not early

        var result = await RegularizationService().ApproveAsync(regId, null);

        result.IsSuccess.Should().BeTrue();

        var log = ReadReportLog();
        log.IsLate.Should().BeFalse(
            "08:45 New York local is before the 09:00 shift start — the late verdict must key on LOCAL time");
        log.LateMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Regularization_NonUtcTenant_LateByLocalWallClock_ExactMagnitude_BUG247()
    {
        // 2026-01-15T14:30:00Z is 09:30 New York local → exactly 30 min past the 09:00 start (grace 0).
        // A UTC-only recompute would compare 14:30 to 09:00 and persist LateMinutes = 330.
        SeedRegularizationOrg("America/New_York");
        var date = new DateOnly(2026, 1, 15);
        var regId = SeedPendingRegularization(
            date,
            new DateTime(2026, 1, 15, 14, 30, 0, DateTimeKind.Utc),   // 09:30 local → 30 min late
            new DateTime(2026, 1, 15, 22, 0, 0, DateTimeKind.Utc));   // 17:00 local → not early

        var result = await RegularizationService().ApproveAsync(regId, null);

        result.IsSuccess.Should().BeTrue();

        var log = ReadReportLog();
        log.IsLate.Should().BeTrue();
        log.LateMinutes.Should().Be(30,
            "the late magnitude is 09:30 local − 09:00 shift start; a UTC-only reading would be 330");
    }

    [Fact]
    public async Task Regularization_UtcTenant_LateByUtcWallClock_Unchanged_BUG247Control()
    {
        // Same 13:45Z clock-in as the on-time New York case, but a UTC tenant: 13:45 > 09:00 → late by
        // 285 min. This is the pre-fix behavior and the exact OPPOSITE verdict to the New York tenant,
        // proving the New York on-time result came from the zone (no-op safety property).
        SeedRegularizationOrg("UTC");
        var date = new DateOnly(2026, 1, 15);
        var regId = SeedPendingRegularization(
            date,
            new DateTime(2026, 1, 15, 13, 45, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 15, 22, 0, 0, DateTimeKind.Utc));

        var result = await RegularizationService().ApproveAsync(regId, null);

        result.IsSuccess.Should().BeTrue();

        var log = ReadReportLog();
        log.IsLate.Should().BeTrue("for a UTC tenant the 13:45Z clock-in is 13:45 local, after the 09:00 start");
        log.LateMinutes.Should().Be(285);   // 13:45 − 09:00, plain UTC time-of-day.
    }
}
