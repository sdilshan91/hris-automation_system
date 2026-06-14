// ============================================================================
// US-ATT-003: Attendance regularization submission service unit tests.
// Covers: missed-both create (AC-1), missed-clock-out linked to an existing log (AC-2/BR-5),
// lookback rejection (AC-3/FR-6/BR-2), duplicate-pending rejection (AC-4/BR-3), locked-payroll
// rejection (AC-5/FR-7/BR-6), future-date rejection (BR-4), inactive-employee rejection, the
// list query, and no-tenant-context rejection. Validator-enforced rules (reason<10, FR-5 time
// consistency) are covered in SubmitRegularizationValidatorTests. Uses EF Core InMemory provider
// through the service (mirrors AttendanceServiceTests).
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

public sealed class AttendanceRegularizationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AttendanceService> _logger;

    private Guid _employeeId;

    public AttendanceRegularizationServiceTests()
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

    // Corrected times are now wall-clock "HH:mm" strings; the service combines date + HH:mm into the
    // stored UTC timestamptz. Callers pass null to omit a time (conditional by regularization type).
    private static SubmitRegularizationRequest Request(
        DateOnly? date = null,
        string type = RegularizationType.MissedBoth,
        string? clockIn = null,
        string? clockOut = null,
        string reason = "Forgot to clock in and out yesterday")
    {
        var d = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        return new SubmitRegularizationRequest
        {
            Date = d,
            RegularizationType = type,
            RequestedClockIn = clockIn,
            RequestedClockOut = clockOut,
            Reason = reason,
        };
    }

    // "HH:mm" wall-clock string for the request, and the matching combined UTC instant the service
    // should persist for that string on the given date.
    private static string Hhmm(int hour, int minute = 0) => $"{hour:D2}:{minute:D2}";

    private static DateTime At(DateOnly date, int hour, int minute = 0)
        => date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    // ── AC-1: missed-both creates a PENDING record with no linked log ───────

    [Fact]
    public async Task Submit_MissedBoth_NoExistingLog_CreatesPendingUnlinked()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));

        var result = await CreateService().SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(RegularizationStatus.Pending);
        result.Value.AttendanceLogId.Should().BeNull();
        result.Value.RegularizationType.Should().Be(RegularizationType.MissedBoth);

        using var db = CreateDbContext();
        var row = db.AttendanceRegularizations.Single();
        row.EmployeeId.Should().Be(_employeeId);
        row.WorkflowInstanceId.Should().BeNull();   // no workflow engine yet (US-ADM-007).
        row.RequestedClockIn.Should().Be(At(date, 9));
        row.RequestedClockOut.Should().Be(At(date, 17));
    }

    // ── AC-2 / BR-5: missed-clock-out links to an existing open log (not mutated) ──

    [Fact]
    public async Task Submit_MissedClockOut_LinksExistingLog_WithoutMutatingIt()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var clockIn = At(date, 9);
        Guid logId;

        using (var db = CreateDbContext())
        {
            logId = BaseEntity.NewUuidV7();
            db.AttendanceLogs.Add(new AttendanceLog
            {
                Id = logId,
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                ClockIn = clockIn,
                ClockOut = null,   // open: clocked in but forgot to clock out.
                Source = "WEB",
            });
            db.SaveChanges();
        }

        var result = await CreateService().SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedClockOut, clockOut: Hhmm(17)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.AttendanceLogId.Should().Be(logId);
        result.Value.RequestedClockIn.Should().BeNull();  // not required for MISSED_CLOCK_OUT.

        // BR-5: the log itself must NOT be mutated on submit (only on approval, US-ATT-004).
        using var verify = CreateDbContext();
        var log = verify.AttendanceLogs.Single(a => a.Id == logId);
        log.ClockOut.Should().BeNull();
        log.Status.Should().BeNull();
    }

    // ── AC-3 / FR-6 / BR-2: lookback window ─────────────────────────────────

    [Fact]
    public async Task Submit_DateOlderThanLookback_IsRejectedWithExactMessage()
    {
        // Default lookback is 7 days; 10 days ago is outside the window.
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));

        var result = await CreateService().SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("lookback_exceeded");
        result.Error.Should().Be("Regularization requests can only be submitted for the last 7 days.");
    }

    [Fact]
    public async Task Submit_DateAtLookbackBoundary_IsAccepted()
    {
        // Exactly 6 days ago is the earliest allowed for a 7-day window (today inclusive).
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6));

        var result = await CreateService().SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)));

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-4 / BR-3: duplicate pending ──────────────────────────────────────

    [Fact]
    public async Task Submit_DuplicatePendingForSameDate_IsRejectedWithExactMessage()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var first = await CreateService().SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)));
        first.IsSuccess.Should().BeTrue();

        var second = await CreateService().SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(8), Hhmm(16)));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("duplicate_pending");
        second.Error.Should().Be("A pending regularization request already exists for this date.");
    }

    // ── AC-5 / FR-7 / BR-6: locked payroll period ───────────────────────────

    [Fact]
    public async Task Submit_DateInLockedPayrollPeriod_IsRejectedWithExactMessage()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));

        using (var db = CreateDbContext())
        {
            db.PayrollLockPeriods.Add(new PayrollLockPeriod
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                StartDate = date.AddDays(-2),
                EndDate = date.AddDays(2),
            });
            db.SaveChanges();
        }

        var result = await CreateService().SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("payroll_period_locked");
        result.Error.Should().Be("This date falls within a locked payroll period. Please contact HR.");
    }

    // ── BR-4: future date ───────────────────────────────────────────────────

    [Fact]
    public async Task Submit_FutureDate_IsRejected()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var result = await CreateService().SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("future_date");
    }

    // ── Inactive employee ───────────────────────────────────────────────────

    [Fact]
    public async Task Submit_TerminatedEmployee_IsRejected()
    {
        var name = Guid.NewGuid().ToString();
        var uid = Guid.NewGuid();
        _currentUser.UserId.Returns(uid);
        using (var db = TestDbContextFactory.Create(_tenantContext, name))
        {
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, UserId = uid,
                EmployeeNo = "EMP-X", FirstName = "T", LastName = "E", Email = "t@e.com",
                Gender = Gender.Male, DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Terminated, IsActive = true,
            });
            db.SaveChanges();
        }

        var svc = new AttendanceService(
            TestDbContextFactory.Create(_tenantContext, name), _tenantContext, _currentUser, _logger);

        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var result = await svc.SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── List query ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyRegularizations_ReturnsOwnRequestsNewestFirst()
    {
        var d1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var d2 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));

        (await CreateService().SubmitRegularizationAsync(
            Request(d2, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)))).IsSuccess.Should().BeTrue();
        (await CreateService().SubmitRegularizationAsync(
            Request(d1, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)))).IsSuccess.Should().BeTrue();

        var result = await CreateService().GetMyRegularizationsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value[0].Date.Should().Be(d1);   // newest date first.
        result.Value[1].Date.Should().Be(d2);
    }

    // ── No tenant context ───────────────────────────────────────────────────

    [Fact]
    public async Task Submit_NoTenantContext_IsRejected()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(false);
        var svc = new AttendanceService(
            TestDbContextFactory.Create(tenantContext, _dbName), tenantContext, _currentUser, _logger);

        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var result = await svc.SubmitRegularizationAsync(
            Request(date, RegularizationType.MissedBoth, Hhmm(9), Hhmm(17)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
