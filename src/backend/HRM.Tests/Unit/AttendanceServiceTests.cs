// ============================================================================
// US-ATT-001: Attendance clock-in service unit tests.
// Covers happy path (AC-1), duplicate-clock-in rejection (AC-2/BR-1), geo-required
// denial (AC-3) and geo-optional success (AC-4), IP allowlist rejection (AC-5/FR-4),
// geo-fence radius rejection (FR-3), photo-required rejection (BR-6), inactive-employee
// rejection (BR-5), IP/user-agent capture (FR-5), and idempotency replay (NFR-4).
// Uses EF Core InMemory provider (mirrors LeaveRequestServiceTests).
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

public sealed class AttendanceServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AttendanceService> _logger;

    private Guid _employeeId;

    public AttendanceServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);

        _logger = Substitute.For<ILogger<AttendanceService>>();

        SeedEmployee(EmployeeStatus.Active);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AttendanceService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser, _logger);

    private void SeedEmployee(EmployeeStatus status)
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
            Status = status,
            IsActive = true,
        });
        db.SaveChanges();
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

    private static ClockInData Data(
        decimal? lat = null, decimal? lon = null, string? photoUrl = null,
        string? ip = "10.0.0.1", string? ua = "Chrome/120", string? key = null)
        => new()
        {
            Latitude = lat,
            Longitude = lon,
            PhotoUrl = photoUrl,
            Source = "WEB",
            IpAddress = ip,
            UserAgent = ua,
            IdempotencyKey = key,
        };

    // ── AC-1: happy path ───────────────────────────────────────────

    [Fact]
    public async Task ClockIn_NoPolicy_SucceedsAndPersistsUtcRecord()
    {
        var svc = CreateService();

        var result = await svc.ClockInAsync(Data());

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeId.Should().Be(_employeeId);
        result.Value.ClockIn.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.ClockOut.Should().BeNull();

        using var db = CreateDbContext();
        db.AttendanceLogs.Should().ContainSingle(a => a.EmployeeId == _employeeId);
    }

    // ── FR-5: IP / user-agent capture ──────────────────────────────

    [Fact]
    public async Task ClockIn_RecordsIpAndUserAgent()
    {
        var svc = CreateService();

        await svc.ClockInAsync(Data(ip: "203.0.113.5", ua: "Firefox/121"));

        using var db = CreateDbContext();
        var log = db.AttendanceLogs.Single();
        log.ClockInIp.Should().Be("203.0.113.5");
        log.ClockInUserAgent.Should().Be("Firefox/121");
    }

    // ── AC-2 / BR-1: duplicate clock-in ────────────────────────────

    [Fact]
    public async Task ClockIn_WhenOpenRecordExists_IsRejected()
    {
        var svc = CreateService();
        (await svc.ClockInAsync(Data())).IsSuccess.Should().BeTrue();

        var second = await CreateService().ClockInAsync(Data());

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_clocked_in");
        second.Error.Should().Be("You have already clocked in. Please clock out first.");
    }

    [Fact]
    public async Task ClockIn_WhenPreviousRecordClosed_Succeeds()
    {
        // Seed an already-closed record; a new open clock-in should be allowed.
        using (var db = CreateDbContext())
        {
            db.AttendanceLogs.Add(new AttendanceLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                ClockIn = DateTime.UtcNow.AddHours(-9),
                ClockOut = DateTime.UtcNow.AddHours(-1),
                Source = "WEB",
            });
            db.SaveChanges();
        }

        var result = await CreateService().ClockInAsync(Data());

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-3 / BR-2: geolocation required but missing ──────────────

    [Fact]
    public async Task ClockIn_GeoRequiredButMissing_IsRejected()
    {
        SeedSettings(s => s.RequireGeolocation = true);

        var result = await CreateService().ClockInAsync(Data());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Location is required");
    }

    [Fact]
    public async Task ClockIn_GeoRequiredAndProvided_Succeeds()
    {
        SeedSettings(s => s.RequireGeolocation = true);

        var result = await CreateService().ClockInAsync(Data(lat: 6.9271m, lon: 79.8612m));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClockInLatitude.Should().Be(6.9271m);
        result.Value.ClockInLongitude.Should().Be(79.8612m);
    }

    // ── AC-4: geolocation optional, none provided ──────────────────

    [Fact]
    public async Task ClockIn_GeoOptional_SucceedsWithoutCoordinates()
    {
        SeedSettings(s => s.RequireGeolocation = false);

        var result = await CreateService().ClockInAsync(Data());

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClockInLatitude.Should().BeNull();
        result.Value.ClockInLongitude.Should().BeNull();
    }

    // ── AC-5 / FR-4: IP allowlist ──────────────────────────────────

    [Fact]
    public async Task ClockIn_IpNotOnAllowlist_IsRejected()
    {
        SeedSettings(s =>
        {
            s.IpAllowlistEnabled = true;
            s.IpAllowlist = new List<string> { "198.51.100.7" };
        });

        var result = await CreateService().ClockInAsync(Data(ip: "203.0.113.9"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("ip_not_allowed");
        result.Error.Should().Be("Clock-in is only allowed from authorized network locations.");
    }

    [Fact]
    public async Task ClockIn_IpOnAllowlist_Succeeds()
    {
        SeedSettings(s =>
        {
            s.IpAllowlistEnabled = true;
            s.IpAllowlist = new List<string> { "198.51.100.7" };
        });

        var result = await CreateService().ClockInAsync(Data(ip: "198.51.100.7"));

        result.IsSuccess.Should().BeTrue();
    }

    // ── FR-3: geo-fence radius ─────────────────────────────────────

    [Fact]
    public async Task ClockIn_OutsideGeoFence_IsRejected()
    {
        SeedSettings(s =>
        {
            s.GeoFenceEnabled = true;
            s.GeoFenceLatitude = 6.9271m;
            s.GeoFenceLongitude = 79.8612m;
            s.GeoFenceRadiusMeters = 100;
        });

        // ~1.5 km away — well outside a 100 m fence.
        var result = await CreateService().ClockInAsync(Data(lat: 6.9400m, lon: 79.8612m));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("geo_fence_violation");
        result.Error.Should().Contain("outside the allowed clock-in area");
    }

    [Fact]
    public async Task ClockIn_InsideGeoFence_Succeeds()
    {
        SeedSettings(s =>
        {
            s.GeoFenceEnabled = true;
            s.GeoFenceLatitude = 6.9271m;
            s.GeoFenceLongitude = 79.8612m;
            s.GeoFenceRadiusMeters = 500;
        });

        // Same point — distance 0.
        var result = await CreateService().ClockInAsync(Data(lat: 6.9271m, lon: 79.8612m));

        result.IsSuccess.Should().BeTrue();
    }

    // ── BR-6: photo required ───────────────────────────────────────

    [Fact]
    public async Task ClockIn_PhotoRequiredButMissing_IsRejected()
    {
        SeedSettings(s => s.RequirePhoto = true);

        var result = await CreateService().ClockInAsync(Data());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("photo is required");
    }

    [Fact]
    public async Task ClockIn_PhotoRequiredAndProvided_Succeeds()
    {
        SeedSettings(s => s.RequirePhoto = true);

        var result = await CreateService().ClockInAsync(Data(photoUrl: "https://blob/selfie.jpg"));

        result.IsSuccess.Should().BeTrue();
    }

    // ── BR-5: inactive employee ────────────────────────────────────

    [Fact]
    public async Task ClockIn_TerminatedEmployee_IsRejected()
    {
        // Re-seed as terminated (fresh DB name to avoid the Active row).
        var svc = new AttendanceService(
            BuildContextWithEmployee(EmployeeStatus.Terminated, out _),
            _tenantContext, _currentUser, _logger);

        var result = await svc.ClockInAsync(Data());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    private AppDbContext BuildContextWithEmployee(EmployeeStatus status, out Guid empId)
    {
        var name = Guid.NewGuid().ToString();
        var uid = Guid.NewGuid();
        _currentUser.UserId.Returns(uid);
        using (var db = TestDbContextFactory.Create(_tenantContext, name))
        {
            empId = Guid.NewGuid();
            db.Employees.Add(new Employee
            {
                Id = empId, TenantId = _tenantId, UserId = uid,
                EmployeeNo = "EMP-X", FirstName = "T", LastName = "E", Email = "t@e.com",
                Gender = Gender.Male, DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = status, IsActive = true,
            });
            db.SaveChanges();
        }
        return TestDbContextFactory.Create(_tenantContext, name);
    }

    // ── NFR-4: idempotency replay ──────────────────────────────────

    [Fact]
    public async Task ClockIn_SameIdempotencyKey_ReturnsSameRecordWithoutDuplicate()
    {
        var key = Guid.NewGuid().ToString();

        var first = await CreateService().ClockInAsync(Data(key: key));
        first.IsSuccess.Should().BeTrue();

        // A replay with the same key returns the cached response (no duplicate, no 409).
        var replay = await CreateService().ClockInAsync(Data(key: key));

        replay.IsSuccess.Should().BeTrue();
        replay.Value!.Id.Should().Be(first.Value!.Id);

        using var db = CreateDbContext();
        db.AttendanceLogs.Count(a => a.EmployeeId == _employeeId).Should().Be(1);
    }

    // ── No tenant context ──────────────────────────────────────────

    [Fact]
    public async Task ClockIn_NoTenantContext_IsRejected()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(false);
        var svc = new AttendanceService(
            TestDbContextFactory.Create(tenantContext, _dbName), tenantContext, _currentUser, _logger);

        var result = await svc.ClockInAsync(Data());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
