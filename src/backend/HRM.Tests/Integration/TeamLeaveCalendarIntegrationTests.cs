// ============================================================================
// US-LV-009: Team Leave Calendar integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (MediatR pipeline + ITenantContext-driven global query filters),
// covering:
//   - GET /api/v1/leaves/team-calendar happy path: a manager sees their team's
//     Approved + Awaiting leaves with full detail (AC-1).
//   - Tenant isolation: a manager in Tenant A does not see Tenant B leaves (Test Hint).
//   - Employee-scope suppression end-to-end: a regular employee never receives
//     Awaiting entries nor leave-type/status detail (BR-1).
//   - Holidays returned for the range (FR-7).
//
// PROVIDER NOTE: same rationale as PendingLeaveQueueIntegrationTests -- the verify
// gate runs `dotnet test` with no PostgreSQL bound, so these use the InMemory
// provider but go through the real composed pipeline (MediatR + query filters),
// which is what proves scope/tenant isolation.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Application.Features.LeaveRequests.Queries;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class TeamLeaveCalendarIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _managerAUser = Guid.NewGuid();   // Mary (Tenant A manager)
    private readonly Guid _employeeAUser = Guid.NewGuid();  // Eve (Tenant A regular employee)
    private readonly Guid _managerBUser = Guid.NewGuid();   // Bea (Tenant B manager)

    private Guid _deptA;
    private Guid _deptB;
    private Guid _leaveTypeA;
    private Guid _leaveTypeB;
    private Guid _managerA;
    private Guid _reportA1;
    private Guid _employeeA;
    private Guid _managerB;
    private Guid _reportB1;

    public TeamLeaveCalendarIntegrationTests()
    {
        SeedData();
    }

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

    private IMediator BuildPipeline(Guid tenantId, Guid userId, params string[] permissions)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Permissions.Returns(permissions);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IHolidayProvider, NoOpHolidayProvider>();
        services.AddScoped<IHolidayService, HolidayService>();
        services.AddScoped<ILeaveNotificationService, LogOnlyLeaveNotificationService>();
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateLeaveRequestCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _deptA = Guid.NewGuid();
        _deptB = Guid.NewGuid();
        _leaveTypeA = Guid.NewGuid();
        _leaveTypeB = Guid.NewGuid();
        _managerA = Guid.NewGuid();
        _reportA1 = Guid.NewGuid();
        _employeeA = Guid.NewGuid();
        _managerB = Guid.NewGuid();
        _reportB1 = Guid.NewGuid();

        db.LeaveTypes.AddRange(
            MakeLeaveType(_leaveTypeA, _tenantA),
            MakeLeaveType(_leaveTypeB, _tenantB));

        db.Employees.AddRange(
            Emp(_managerA, _tenantA, _managerAUser, "Mary", _deptA, null),
            Emp(_reportA1, _tenantA, null, "Ann", _deptA, _managerA),
            Emp(_employeeA, _tenantA, _employeeAUser, "Eve", _deptA, _managerA),
            Emp(_managerB, _tenantB, _managerBUser, "Bea", _deptB, null),
            Emp(_reportB1, _tenantB, null, "Ben", _deptB, _managerB));

        db.LeaveRequests.AddRange(
            Leave(_reportA1, _leaveTypeA, _tenantA, LeaveRequestStatus.Approved),
            Leave(_reportA1, _leaveTypeA, _tenantA, LeaveRequestStatus.Pending, Base.AddDays(5)),
            Leave(_reportB1, _leaveTypeB, _tenantB, LeaveRequestStatus.Approved));

        // FR-7: a public holiday in Tenant A's range.
        db.Holidays.Add(new Holiday
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, Name = "Founders Day",
            Date = Base.AddDays(3), Type = HolidayType.Public, IsActive = true, IsRecurring = false,
        });

        db.SaveChanges();
    }

    private static readonly DateOnly Base = new(2026, 6, 1);

    private static LeaveType MakeLeaveType(Guid id, Guid tenantId) => new()
    {
        Id = id, TenantId = tenantId, Name = "Annual Leave", Color = "#4CAF50",
        AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront,
        Gender = LeaveTypeGender.All, IsActive = true,
    };

    private static Employee Emp(Guid id, Guid tenantId, Guid? userId, string first, Guid deptId, Guid? reportsTo) => new()
    {
        Id = id, TenantId = tenantId, UserId = userId, EmployeeNo = $"E-{first}",
        FirstName = first, LastName = "X", Email = $"{first}@t.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = deptId,
        JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active, ReportsToEmployeeId = reportsTo, IsActive = true,
    };

    private static LeaveRequest Leave(Guid empId, Guid leaveTypeId, Guid tenantId,
        LeaveRequestStatus status, DateOnly? start = null) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId,
        LeaveTypeId = leaveTypeId, StartDate = start ?? Base, EndDate = (start ?? Base).AddDays(1),
        TotalDays = 2m, Status = status, RequestedAt = DateTime.UtcNow,
    };

    private static GetTeamLeaveCalendarQuery Query(string? status = null) =>
        new(new TeamLeaveCalendarQueryParams { From = Base, To = Base.AddDays(30), Status = status });

    // -- Happy path: manager sees own team's Approved + Awaiting with detail + holidays --

    [Fact]
    public async Task TeamCalendar_HappyPath_ManagerSeesTeamWithDetailAndHolidays()
    {
        var mediator = BuildPipeline(_tenantA, _managerAUser);

        var result = await mediator.Send(Query());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Scope.Should().Be("Manager");
        result.Value.Entries.Should().HaveCount(2);
        result.Value.Entries.Should().OnlyContain(e => e.EmployeeId == _reportA1);
        result.Value.Entries.Should().Contain(e => e.Status == "Approved" && e.LeaveTypeName == "Annual Leave");
        result.Value.Entries.Should().Contain(e => e.Status == "Pending");
        // FR-7 holidays in range.
        result.Value.Holidays.Should().ContainSingle(h => h.Name == "Founders Day");
    }

    // -- Tenant isolation: Tenant A manager cannot see Tenant B leaves --

    [Fact]
    public async Task TeamCalendar_DoesNotLeakOtherTenantLeaves()
    {
        var managerA = BuildPipeline(_tenantA, _managerAUser);
        var managerB = BuildPipeline(_tenantB, _managerBUser);

        var calA = await managerA.Send(Query());
        var calB = await managerB.Send(Query());

        calA.Value!.Entries.Should().OnlyContain(e => e.EmployeeId == _reportA1);
        calB.Value!.Entries.Should().OnlyContain(e => e.EmployeeId == _reportB1);
        // Tenant B has no holiday seeded.
        calB.Value.Holidays.Should().BeEmpty();
    }

    // -- BR-1 end-to-end: employee scope suppresses detail and Awaiting entries --

    [Fact]
    public async Task TeamCalendar_EmployeeScope_SuppressesDetailAndAwaiting()
    {
        // Eve is a regular employee in dept A (reports to Mary but has no direct reports of her own).
        var mediator = BuildPipeline(_tenantA, _employeeAUser);

        var result = await mediator.Send(Query());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Scope.Should().Be("Employee");

        // Only the Approved colleague leave is visible; the Awaiting one is excluded (BR-1).
        result.Value.Entries.Should().HaveCount(1);
        var row = result.Value.Entries.Single();
        row.EmployeeId.Should().Be(_reportA1);
        // Suppressed fields must NOT be present in the employee response.
        row.LeaveTypeName.Should().BeNull();
        row.Color.Should().BeNull();
        row.Status.Should().BeNull();
    }
}
