// US-ATT-001: GET /api/v1/attendance/status - clock-status query unit tests.
// Covers not-clocked-in vs clocked-in (open AttendanceLog / BR-1), RequireGeolocation
// true/false (BR-2), null-settings default, null shift fields (US-ATT-005 not built),
// no-employee (403) / no-tenant (400), and multi-tenant isolation.
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class GetClockStatusTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AttendanceService> _logger;

    private Guid _employeeId;

    public GetClockStatusTests()
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

    private void SeedOpenLog(DateTime clockInUtc)
    {
        using var db = CreateDbContext();
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            ClockIn = clockInUtc,
            ClockOut = null,
            Source = "WEB",
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task GetStatus_NoOpenRecord_ReturnsNotClockedIn()
    {
        var result = await CreateService().GetClockStatusAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsClockedIn.Should().BeFalse();
        result.Value.ClockedInAt.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_OnlyClosedRecord_ReturnsNotClockedIn()
    {
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

        var result = await CreateService().GetClockStatusAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsClockedIn.Should().BeFalse();
        result.Value.ClockedInAt.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_OpenRecord_ReturnsClockedInWithUtcInstant()
    {
        var clockIn = new DateTime(2026, 6, 14, 8, 30, 0, DateTimeKind.Utc);
        SeedOpenLog(clockIn);

        var result = await CreateService().GetClockStatusAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsClockedIn.Should().BeTrue();
        result.Value.ClockedInAt.Should().Be(clockIn);
    }

    [Fact]
    public async Task GetStatus_RequireGeolocationTrue_IsReturnedTrue()
    {
        SeedSettings(s => s.RequireGeolocation = true);

        var result = await CreateService().GetClockStatusAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequireGeolocation.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatus_RequireGeolocationFalse_IsReturnedFalse()
    {
        SeedSettings(s => s.RequireGeolocation = false);

        var result = await CreateService().GetClockStatusAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequireGeolocation.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_NoSettingsRow_DefaultsRequireGeolocationFalse_AndDoesNotCreateRow()
    {
        var result = await CreateService().GetClockStatusAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequireGeolocation.Should().BeFalse();

        using var db = CreateDbContext();
        db.AttendanceSettings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatus_ShiftFieldsAreNull()
    {
        SeedOpenLog(DateTime.UtcNow);

        var result = await CreateService().GetClockStatusAsync();

        result.Value!.ShiftName.Should().BeNull();
        result.Value.ShiftStart.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_NoEmployeeLinkedToUser_IsRejected403()
    {
        var otherUser = Substitute.For<ICurrentUser>();
        otherUser.UserId.Returns(Guid.NewGuid());
        var svc = new AttendanceService(CreateDbContext(), _tenantContext, otherUser, _logger);

        var result = await svc.GetClockStatusAsync();

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetStatus_NoTenantContext_IsRejected400()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(false);
        var svc = new AttendanceService(
            TestDbContextFactory.Create(tenantContext, _dbName), tenantContext, _currentUser, _logger);

        var result = await svc.GetClockStatusAsync();

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetStatus_OpenRecordInAnotherTenant_DoesNotLeak()
    {
        // Another tenant has an open record for the same user id. The global query filter must
        // scope to the current tenant, so our employee (no open record) is reported not clocked in.
        var otherTenantId = Guid.NewGuid();
        var otherTenantContext = Substitute.For<ITenantContext>();
        otherTenantContext.TenantId.Returns(otherTenantId);
        otherTenantContext.IsResolved.Returns(true);
        otherTenantContext.IsSystemContext.Returns(false);

        using (var otherDb = TestDbContextFactory.Create(otherTenantContext, _dbName))
        {
            var otherEmployeeId = Guid.NewGuid();
            otherDb.Employees.Add(new Employee
            {
                Id = otherEmployeeId,
                TenantId = otherTenantId,
                UserId = _userId,
                EmployeeNo = "EMP-OTHER",
                FirstName = "Jane",
                LastName = "Roe",
                Email = "jane@other.com",
                Gender = Gender.Female,
                DateOfJoining = new DateTime(2021, 1, 1),
                DepartmentId = Guid.NewGuid(),
                JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active,
                IsActive = true,
            });
            otherDb.AttendanceLogs.Add(new AttendanceLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = otherTenantId,
                EmployeeId = otherEmployeeId,
                ClockIn = DateTime.UtcNow,
                ClockOut = null,
                Source = "WEB",
            });
            otherDb.SaveChanges();
        }

        var result = await CreateService().GetClockStatusAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsClockedIn.Should().BeFalse();
    }
}
