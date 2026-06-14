// ============================================================================
// US-LV-009: Team Leave Calendar (manager / employee / HR views) unit tests.
//
// Covers the row-level scope resolution and the BR-1 data-suppression rule:
//   - Manager sees direct reports' Approved + Awaiting with full detail (AC-1, BR-2).
//   - Employee sees only own-department Approved leaves with leave-type/status SUPPRESSED
//     and NO Awaiting entries -- the key BR-1 data-leak guard (AC-2).
//   - HR (Leave.View.All) sees all employees' leaves, full detail (BR-3).
//   - Cancelled (and Rejected) excluded (BR-4).
//   - A manager does not see another manager's team (Test Hint).
//   - Public holidays are returned for the range (FR-7).
//   - Half-day passthrough (BR-5).
//   - Empty calendar when the user has no employee record.
// Uses EF Core InMemory provider (mirrors PendingLeaveQueueServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class TeamLeaveCalendarServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IHolidayProvider _holidayProvider;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILogger<LeaveRequestService> _logger;
    private readonly ILogger<HolidayService> _holidayLogger;

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _otherManagerUserId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();

    private Guid _deptEngId;
    private Guid _deptSalesId;
    private Guid _annualLeaveTypeId;
    private Guid _sickLeaveTypeId;

    private Guid _managerId;       // Mary, Engineering, no manager
    private Guid _report1Id;       // Rita, reports to Mary, Engineering
    private Guid _report2Id;       // Raj, reports to Mary, Engineering
    private Guid _otherManagerId;  // Mike, Sales, no manager
    private Guid _otherReportId;   // Oscar, reports to Mike, Sales
    private Guid _employeeId;      // Eve, Engineering, reports to Mary (regular employee viewer)
    private Guid _hrId;            // Holly, HR

    public TeamLeaveCalendarServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _holidayProvider = new NoOpHolidayProvider();
        _notificationService = Substitute.For<ILeaveNotificationService>();
        _logger = Substitute.For<ILogger<LeaveRequestService>>();
        _holidayLogger = Substitute.For<ILogger<HolidayService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private static ICurrentUser User(Guid userId, params string[] permissions)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.Permissions.Returns(permissions);
        return u;
    }

    private LeaveRequestService CreateService(ICurrentUser user)
    {
        var db = CreateDbContext();
        var holidayService = new HolidayService(db, _tenantContext, user, _holidayLogger);
        return new LeaveRequestService(
            db, _tenantContext, user, _holidayProvider, _notificationService, _logger, holidayService);
    }

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        _deptEngId = Guid.NewGuid();
        _deptSalesId = Guid.NewGuid();

        db.LeaveTypes.AddRange(
            new LeaveType
            {
                Id = _annualLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Annual Leave", Color = "#4CAF50", AnnualEntitlement = 14,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All, IsActive = true,
            },
            new LeaveType
            {
                Id = _sickLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Sick Leave", Color = "#F44336", AnnualEntitlement = 7,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All, IsActive = true,
            });

        var manager = NewEmployee(_managerId = Guid.NewGuid(), "Mary", _managerUserId, _deptEngId, reportsTo: null);
        var report1 = NewEmployee(_report1Id = Guid.NewGuid(), "Rita", null, _deptEngId, reportsTo: _managerId);
        var report2 = NewEmployee(_report2Id = Guid.NewGuid(), "Raj", null, _deptEngId, reportsTo: _managerId);
        var otherMgr = NewEmployee(_otherManagerId = Guid.NewGuid(), "Mike", _otherManagerUserId, _deptSalesId, reportsTo: null);
        var otherRep = NewEmployee(_otherReportId = Guid.NewGuid(), "Oscar", null, _deptSalesId, reportsTo: _otherManagerId);
        var employee = NewEmployee(_employeeId = Guid.NewGuid(), "Eve", _employeeUserId, _deptEngId, reportsTo: _managerId);
        var hr = NewEmployee(_hrId = Guid.NewGuid(), "Holly", _hrUserId, _deptSalesId, reportsTo: null);

        db.Employees.AddRange(manager, report1, report2, otherMgr, otherRep, employee, hr);
        db.SaveChanges();
    }

    private Employee NewEmployee(Guid id, string first, Guid? userId, Guid departmentId, Guid? reportsTo) => new()
    {
        Id = id,
        TenantId = _tenantId,
        UserId = userId,
        EmployeeNo = $"EMP-{id.ToString()[..4]}",
        FirstName = first,
        LastName = "Test",
        Email = $"{first}@test.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = departmentId,
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active,
        ReportsToEmployeeId = reportsTo,
        IsActive = true,
    };

    private Guid SeedRequest(
        Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end,
        LeaveRequestStatus status = LeaveRequestStatus.Approved,
        bool isHalfDay = false, string? halfDaySession = null, decimal totalDays = 1m)
    {
        using var db = CreateDbContext();
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
            StartDate = start, EndDate = end, TotalDays = totalDays, Status = status,
            IsHalfDay = isHalfDay, HalfDaySession = halfDaySession,
            RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        return id;
    }

    // Convenience: an Awaiting-status (not-yet-decided) request.
    private Guid SeedAwaiting(Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end)
        => SeedRequest(employeeId, leaveTypeId, start, end, LeaveRequestStatus.Pending);

    private void SeedHoliday(DateOnly date, string name = "Public Holiday", HolidayType type = HolidayType.Public)
    {
        using var db = CreateDbContext();
        db.Holidays.Add(new Holiday
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = name,
            Date = date, Type = type, IsActive = true, IsRecurring = false,
        });
        db.SaveChanges();
    }

    private static TeamLeaveCalendarQueryParams Params(
        DateOnly? from = null, DateOnly? to = null,
        Guid? employeeId = null, Guid? leaveTypeId = null, string? status = null) => new()
    {
        From = from ?? Base,
        To = to ?? Base.AddDays(30),
        EmployeeId = employeeId,
        LeaveTypeId = leaveTypeId,
        Status = status,
    };

    private static readonly DateOnly Base = new(2026, 6, 1);

    private const string AwaitingStatus = "Pending";

    // -- AC-1 / BR-2: manager sees direct reports' Approved + Awaiting, full detail --

    [Fact]
    public async Task Manager_SeesDirectReportsApprovedAndAwaiting_WithFullDetail()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);
        SeedAwaiting(_report2Id, _sickLeaveTypeId, Base.AddDays(5), Base.AddDays(6));

        var result = await CreateService(User(_managerUserId)).GetTeamLeaveCalendarAsync(Params());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Scope.Should().Be("Manager");
        result.Value.Entries.Should().HaveCount(2);
        result.Value.Entries.Should().Contain(e =>
            e.EmployeeId == _report1Id && e.Status == "Approved"
            && e.LeaveTypeName == "Annual Leave" && e.Color == "#4CAF50");
        result.Value.Entries.Should().Contain(e =>
            e.EmployeeId == _report2Id && e.Status == AwaitingStatus
            && e.LeaveTypeName == "Sick Leave" && e.Color == "#F44336");
    }

    // -- Test Hint: manager does NOT see another manager's team --

    [Fact]
    public async Task Manager_DoesNotSeeOtherManagersTeam()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);
        SeedRequest(_otherReportId, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);

        var result = await CreateService(User(_managerUserId)).GetTeamLeaveCalendarAsync(Params());

        result.Value!.Entries.Should().OnlyContain(e => e.EmployeeId == _report1Id);
    }

    // -- AC-2 / BR-1: employee sees only department Approved, detail suppressed, no Awaiting --

    [Fact]
    public async Task Employee_SeesOnlyDepartmentApproved_WithTypeAndStatusSuppressed()
    {
        // Same-department colleague approved leave (should show, suppressed).
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);
        // Same-department colleague Awaiting (must NOT show -- BR-1 leak guard).
        SeedAwaiting(_report2Id, _sickLeaveTypeId, Base.AddDays(5), Base.AddDays(6));
        // Other-department approved (must NOT show -- not Eve's department).
        SeedRequest(_otherReportId, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);

        var result = await CreateService(User(_employeeUserId)).GetTeamLeaveCalendarAsync(Params());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Scope.Should().Be("Employee");

        // Only the approved engineering colleague is visible; Awaiting and other-dept excluded.
        result.Value.Entries.Should().OnlyContain(e => e.EmployeeId == _report1Id);

        var row = result.Value.Entries.Single();
        // BR-1 suppression: no leave type, no colour, no status leaked.
        row.LeaveTypeName.Should().BeNull();
        row.Color.Should().BeNull();
        row.Status.Should().BeNull();
        // Still carries enough to render "on leave".
        row.StartDate.Should().Be(Base);
        row.EndDate.Should().Be(Base.AddDays(2));
    }

    [Fact]
    public async Task Employee_NeverSeesAnyAwaitingEntries_EvenWithStatusFilter()
    {
        SeedAwaiting(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2));

        // Even if a status filter is passed, employee scope ignores it and never returns Awaiting.
        var result = await CreateService(User(_employeeUserId))
            .GetTeamLeaveCalendarAsync(Params(status: AwaitingStatus));

        result.Value!.Entries.Should().BeEmpty();
    }

    // -- BR-3: HR with Leave.View.All sees all employees' leaves, full detail --

    [Fact]
    public async Task HrWithViewAll_SeesAllTeamsApprovedAndAwaiting_WithDetail()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);
        SeedAwaiting(_otherReportId, _sickLeaveTypeId, Base.AddDays(3), Base.AddDays(4));

        var result = await CreateService(User(_hrUserId, PermissionCatalog.Leave.ViewAll))
            .GetTeamLeaveCalendarAsync(Params());

        result.Value!.Scope.Should().Be("All");
        result.Value.Entries.Should().HaveCount(2);
        result.Value.Entries.Should().Contain(e => e.EmployeeId == _report1Id && e.LeaveTypeName == "Annual Leave");
        result.Value.Entries.Should().Contain(e => e.EmployeeId == _otherReportId && e.Status == AwaitingStatus);
    }

    // -- BR-4: Cancelled (and Rejected) excluded --

    [Fact]
    public async Task Manager_ExcludesCancelledAndRejected()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);
        SeedRequest(_report1Id, _annualLeaveTypeId, Base.AddDays(5), Base.AddDays(6), LeaveRequestStatus.Cancelled);
        SeedRequest(_report2Id, _annualLeaveTypeId, Base.AddDays(8), Base.AddDays(9), LeaveRequestStatus.Rejected);

        var result = await CreateService(User(_managerUserId)).GetTeamLeaveCalendarAsync(Params());

        result.Value!.Entries.Should().HaveCount(1);
        result.Value.Entries.Single().Status.Should().Be("Approved");
    }

    // -- FR-7: holidays returned for the range --

    [Fact]
    public async Task ReturnsPublicHolidaysInRange_ForBackgroundHighlights()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);
        SeedHoliday(Base.AddDays(10), "Midsummer");
        SeedHoliday(Base.AddDays(100), "Out of range");          // outside [from, to]
        SeedHoliday(Base.AddDays(11), "Restricted", HolidayType.Restricted); // not Public

        var result = await CreateService(User(_managerUserId)).GetTeamLeaveCalendarAsync(Params());

        result.Value!.Holidays.Should().ContainSingle(h => h.Name == "Midsummer");
        result.Value.Holidays.Should().OnlyContain(h => h.Date >= Base && h.Date <= Base.AddDays(30));
    }

    // -- BR-5: half-day passthrough --

    [Fact]
    public async Task Manager_PassesThroughHalfDayDetail()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base, LeaveRequestStatus.Approved,
            isHalfDay: true, halfDaySession: "AM", totalDays: 0.5m);

        var result = await CreateService(User(_managerUserId)).GetTeamLeaveCalendarAsync(Params());

        var row = result.Value!.Entries.Single();
        row.IsHalfDay.Should().BeTrue();
        row.HalfDaySession.Should().Be("AM");
        row.TotalDays.Should().Be(0.5m);
    }

    // -- FR-6: leave-type filter (manager scope) --

    [Fact]
    public async Task Manager_FilterByLeaveType()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1), LeaveRequestStatus.Approved);
        SeedRequest(_report2Id, _sickLeaveTypeId, Base, Base.AddDays(1), LeaveRequestStatus.Approved);

        var result = await CreateService(User(_managerUserId))
            .GetTeamLeaveCalendarAsync(Params(leaveTypeId: _sickLeaveTypeId));

        result.Value!.Entries.Should().OnlyContain(e => e.LeaveTypeName == "Sick Leave");
    }

    // -- Range filtering: only entries overlapping [from, to] --

    [Fact]
    public async Task Manager_ReturnsOnlyEntriesOverlappingRange()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);    // in range
        SeedRequest(_report2Id, _annualLeaveTypeId, Base.AddDays(60), Base.AddDays(62), LeaveRequestStatus.Approved); // out

        var result = await CreateService(User(_managerUserId))
            .GetTeamLeaveCalendarAsync(Params(from: Base, to: Base.AddDays(30)));

        result.Value!.Entries.Should().OnlyContain(e => e.EmployeeId == _report1Id);
    }

    // -- Empty calendar when the user has no employee record --

    [Fact]
    public async Task NoEmployeeRecord_ReturnsEmptyCalendar()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Approved);

        var result = await CreateService(User(Guid.NewGuid())).GetTeamLeaveCalendarAsync(Params());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Entries.Should().BeEmpty();
        result.Value.Holidays.Should().BeEmpty();
    }

    // -- Invalid range guard --

    [Fact]
    public async Task ToBeforeFrom_Fails()
    {
        var result = await CreateService(User(_managerUserId))
            .GetTeamLeaveCalendarAsync(Params(from: Base.AddDays(10), to: Base));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
