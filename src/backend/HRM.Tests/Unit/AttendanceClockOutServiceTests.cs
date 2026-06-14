// ============================================================================
// US-ATT-002: Attendance clock-out service unit tests.
// Covers happy path + correct total (AC-1), no-open-record rejection (AC-2/BR-1),
// auto-break deduction (FR-3/BR-2), overtime detection (FR-4/BR-3/AC-3),
// short-day detection (BR-4/AC-4), >16h anomaly (FR-7/BR-6), geolocation capture
// (AC-5/FR-6), geo-required-but-missing rejection, server-side UTC clock-out (FR-1),
// and the LastCompleted summary on the status query.
// Uses EF Core InMemory provider (mirrors AttendanceServiceTests).
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

public sealed class AttendanceClockOutServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AttendanceService> _logger;

    private Guid _employeeId;

    public AttendanceClockOutServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);

        _logger = Substitute.For<ILogger<AttendanceService>>();

        SeedEmployee();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AttendanceService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser, _logger);

    private void SeedEmployee()
    {
        using var db = CreateDbContext();
        db.Employees.Add(new Employee
        {
            Id = _employeeId = Guid.NewGuid(),
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
        db.SaveChanges();
    }

    /// <summary>Seeds an open clock-in for the employee, clocked in <paramref name="agoMinutes"/> ago.</summary>
    private Guid SeedOpenLog(int agoMinutes)
    {
        var id = BaseEntity.NewUuidV7();
        using var db = CreateDbContext();
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = id,
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            ClockIn = DateTime.UtcNow.AddMinutes(-agoMinutes),
            Source = "WEB",
        });
        db.SaveChanges();
        return id;
    }

    private void SeedSettings(Action<AttendanceSettings> configure)
    {
        using var db = CreateDbContext();
        var settings = new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
        };
        configure(settings);
        db.AttendanceSettings.Add(settings);
        db.SaveChanges();
    }

    private static ClockOutData Data(
        decimal? lat = null, decimal? lon = null, string? ip = "10.0.0.1", string? ua = "Chrome/120")
        => new() { Latitude = lat, Longitude = lon, IpAddress = ip, UserAgent = ua };

    // ── AC-1: happy path with correct total ────────────────────────

    [Fact]
    public async Task ClockOut_NoPolicy_ClosesRecordWithUtcAndTotal()
    {
        // 8h session, default 60min break above 6h threshold => 480 - 60 = 420 net, COMPLETE.
        SeedOpenLog(agoMinutes: 480);

        var result = await CreateService().ClockOutAsync(Data());

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClockOut.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.TotalWorkMinutes.Should().Be(420);
        result.Value.OvertimeMinutes.Should().Be(0);
        result.Value.Status.Should().Be("COMPLETE");

        using var db = CreateDbContext();
        var log = db.AttendanceLogs.Single();
        log.ClockOut.Should().NotBeNull();
        log.TotalWorkMinutes.Should().Be(420);
        log.Status.Should().Be("COMPLETE");
    }

    // ── AC-2 / BR-1: no open record ────────────────────────────────

    [Fact]
    public async Task ClockOut_NoOpenRecord_IsRejected()
    {
        var result = await CreateService().ClockOutAsync(Data());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("no_active_clock_in");
        result.Error.Should().Be(
            "No active clock-in found. Please clock in first or submit a regularization request.");
    }

    [Fact]
    public async Task ClockOut_OnlyClosedRecordsExist_IsRejected()
    {
        // A previously closed record must not be re-closed.
        using (var db = CreateDbContext())
        {
            db.AttendanceLogs.Add(new AttendanceLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                ClockIn = DateTime.UtcNow.AddHours(-9),
                ClockOut = DateTime.UtcNow.AddHours(-1),
                Status = "COMPLETE",
                Source = "WEB",
            });
            db.SaveChanges();
        }

        var result = await CreateService().ClockOutAsync(Data());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── FR-3 / BR-2: auto-break deduction ──────────────────────────

    [Fact]
    public async Task ClockOut_AutoBreakDeducted_WhenAboveThreshold()
    {
        // 7h (420 min) > 360 threshold => deduct 60 => 360 net.
        SeedOpenLog(agoMinutes: 420);

        var result = await CreateService().ClockOutAsync(Data());

        result.Value!.TotalWorkMinutes.Should().Be(360);
    }

    [Fact]
    public async Task ClockOut_NoBreakDeducted_WhenBelowThreshold()
    {
        // 5h (300 min) <= 360 threshold => no break deduction. But < 240 minimum? No: 300 >= 240.
        SeedOpenLog(agoMinutes: 300);

        var result = await CreateService().ClockOutAsync(Data());

        result.Value!.TotalWorkMinutes.Should().Be(300);
        result.Value.Status.Should().Be("COMPLETE");
    }

    // ── FR-4 / BR-3 / AC-3: overtime detection ─────────────────────

    [Fact]
    public async Task ClockOut_OvertimeDetected_TenHoursOnEightHourStandard()
    {
        // Test-hint: 10h on an 8h shift => 120 OT minutes.
        // 600 gross - 60 break = 540 net; 540 - 480 standard = 60 OT.
        // To get exactly 120 OT we disable the break so net == gross.
        SeedSettings(s =>
        {
            s.StandardWorkMinutes = 480;
            s.AutoBreakMinutes = 0;          // isolate overtime math from break math
            s.AutoBreakThresholdMinutes = 360;
        });
        SeedOpenLog(agoMinutes: 600);        // 10h

        var result = await CreateService().ClockOutAsync(Data());

        result.Value!.TotalWorkMinutes.Should().Be(600);
        result.Value.OvertimeMinutes.Should().Be(120);
        result.Value.Status.Should().Be("OVERTIME");
    }

    // ── BR-4 / AC-4: short day ──────────────────────────────────────

    [Fact]
    public async Task ClockOut_ShortDay_BelowMinimum()
    {
        // 3h (180 min) < 240 minimum => SHORT_DAY (and below break threshold, no deduction).
        SeedOpenLog(agoMinutes: 180);

        var result = await CreateService().ClockOutAsync(Data());

        result.Value!.TotalWorkMinutes.Should().Be(180);
        result.Value.Status.Should().Be("SHORT_DAY");
    }

    // ── FR-7 / BR-6: anomaly > 16h ─────────────────────────────────

    [Fact]
    public async Task ClockOut_Anomaly_WhenSpanExceeds16Hours()
    {
        SeedOpenLog(agoMinutes: 17 * 60);    // 17h

        var result = await CreateService().ClockOutAsync(Data());

        result.Value!.Status.Should().Be("ANOMALY");
    }

    // ── AC-5 / FR-6: geolocation capture ───────────────────────────

    [Fact]
    public async Task ClockOut_CapturesGeolocation_WhenProvided()
    {
        SeedOpenLog(agoMinutes: 480);

        await CreateService().ClockOutAsync(Data(lat: 6.9271m, lon: 79.8612m, ip: "203.0.113.5"));

        using var db = CreateDbContext();
        var log = db.AttendanceLogs.Single();
        log.ClockOutLatitude.Should().Be(6.9271m);
        log.ClockOutLongitude.Should().Be(79.8612m);
        log.ClockOutIp.Should().Be("203.0.113.5");
    }

    [Fact]
    public async Task ClockOut_GeoRequiredButMissing_IsRejected()
    {
        SeedSettings(s => s.RequireGeolocation = true);
        SeedOpenLog(agoMinutes: 480);

        var result = await CreateService().ClockOutAsync(Data());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Location is required");
    }

    [Fact]
    public async Task ClockOut_GeoRequiredAndProvided_Succeeds()
    {
        SeedSettings(s => s.RequireGeolocation = true);
        SeedOpenLog(agoMinutes: 480);

        var result = await CreateService().ClockOutAsync(Data(lat: 6.9271m, lon: 79.8612m));

        result.IsSuccess.Should().BeTrue();
    }

    // ── No tenant context ──────────────────────────────────────────

    [Fact]
    public async Task ClockOut_NoTenantContext_IsRejected()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(false);
        var svc = new AttendanceService(
            TestDbContextFactory.Create(tenantContext, _dbName), tenantContext, _currentUser, _logger);

        var result = await svc.ClockOutAsync(Data());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── Status query: LastCompleted summary (US-ATT-002) ───────────

    [Fact]
    public async Task GetClockStatus_AfterClockOut_ReturnsLastCompletedSummary()
    {
        SeedOpenLog(agoMinutes: 480);
        var clockOut = await CreateService().ClockOutAsync(Data());
        clockOut.IsSuccess.Should().BeTrue();

        var status = await CreateService().GetClockStatusAsync();

        status.IsSuccess.Should().BeTrue();
        status.Value!.IsClockedIn.Should().BeFalse();
        status.Value.LastCompleted.Should().NotBeNull();
        status.Value.LastCompleted!.TotalWorkMinutes.Should().Be(420);
        status.Value.LastCompleted.Status.Should().Be("COMPLETE");
    }
}
