// ============================================================================
// US-LV-011: Compulsory Leave / Loss of Pay (LOP) — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (MediatR pipeline + ITenantContext-driven global query filters),
// covering:
//   - POST /api/v1/leaves/assign-lop end-to-end: HrAssigned, is_lop=true rows persisted (AC-3).
//   - GET /api/v1/leaves/lop-summary returns the assigned LOP data (FR-5).
//   - POST /api/v1/leaves/compulsory bulk assign end-to-end: deduct-or-LOP per balance (FR-6/BR-4).
//   - Tenant isolation: Tenant A LOP rows are not visible to Tenant B (NFR-2).
//
// PROVIDER NOTE: same rationale as LeaveCancellationIntegrationTests — the verify gate runs
// `dotnet test` with no PostgreSQL bound, so these use the InMemory provider but go through the
// real composed pipeline (MediatR + query filters), which is what proves tenant isolation.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Application.Features.LeaveRequests.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class LopIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _hrAUser = Guid.NewGuid();
    private readonly Guid _hrBUser = Guid.NewGuid();

    private Guid _leaveTypeA;
    private Guid _leaveTypeB;
    private Guid _employeeA;
    private Guid _employeeB;

    private static readonly DateOnly Day1 = new(2026, 3, 2); // Monday
    private static readonly DateOnly Day2 = new(2026, 3, 3);
    private static readonly DateOnly Day3 = new(2026, 3, 4);

    public LopIntegrationTests()
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

    private IMediator BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IHolidayProvider, NoOpHolidayProvider>();
        services.AddScoped<IAttendanceProvider, NoOpAttendanceProvider>();
        services.AddScoped<ILeaveNotificationService, LogOnlyLeaveNotificationService>();
        services.AddScoped<ILeaveTypeService, LeaveTypeService>();
        services.AddScoped<ILopService, LopService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateLeaveRequestCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private AppDbContext DbFor(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = tenantId });
    }

    private void SeedData()
    {
        _leaveTypeA = Guid.NewGuid();
        _leaveTypeB = Guid.NewGuid();
        _employeeA = Guid.NewGuid();
        _employeeB = Guid.NewGuid();

        using var db = DbFor(_tenantA);

        db.LeaveTypes.AddRange(
            MakeLeaveType(_leaveTypeA, _tenantA),
            MakeLeaveType(_leaveTypeB, _tenantB));

        db.Employees.AddRange(
            Emp(_employeeA, _tenantA, _hrAUser, "Ann"),
            Emp(_employeeB, _tenantB, _hrBUser, "Ben"));

        db.SaveChanges();
    }

    private static LeaveType MakeLeaveType(Guid id, Guid tenantId) => new()
    {
        Id = id, TenantId = tenantId, Name = "Annual Leave", Color = "#4CAF50",
        AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront,
        Gender = LeaveTypeGender.All, NegativeBalanceAllowed = false, IsActive = true,
    };

    private static Employee Emp(Guid id, Guid tenantId, Guid? userId, string first) => new()
    {
        Id = id, TenantId = tenantId, UserId = userId, EmployeeNo = $"E-{first}",
        FirstName = first, LastName = "X", Email = $"{first}@t.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active, IsActive = true,
    };

    private void SeedBalance(Guid empId, Guid leaveTypeId, Guid tenantId, decimal balance)
    {
        using var db = DbFor(tenantId);
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EntryType = LedgerEntryType.Accrual,
            EmployeeId = empId, LeaveTypeId = leaveTypeId, LeaveYear = Day1.Year,
            Amount = balance, BalanceAfter = balance, OccurredAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    // ── AC-3: assign-lop end-to-end ────────────────────────────────

    [Fact]
    public async Task AssignLop_EndToEnd_PersistsHrAssignedLopRows()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser);

        var result = await mediator.Send(new AssignLopCommand(new AssignLopRequest
        {
            EmployeeId = _employeeA,
            Dates = [Day1, Day2],
            Reason = "Unauthorised absence",
        }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.CreatedCount.Should().Be(2);

        using var db = DbFor(_tenantA);
        var rows = db.LeaveRequests.Where(lr => lr.EmployeeId == _employeeA && lr.IsLop).ToList();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(lr => lr.Status == LeaveRequestStatus.HrAssigned);
        rows.Should().OnlyContain(lr => lr.LopSource == LopSource.HrAssigned);
    }

    // ── FR-5: lop-summary end-to-end ───────────────────────────────

    [Fact]
    public async Task GetLopSummary_EndToEnd_ReturnsAssignedLopData()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser);

        await mediator.Send(new AssignLopCommand(new AssignLopRequest
        {
            EmployeeId = _employeeA,
            Dates = [Day1, Day2],
            Reason = "Absence",
        }));

        var summary = await mediator.Send(new GetLopSummaryQuery(_employeeA, Day1, Day3));

        summary.IsSuccess.Should().BeTrue();
        summary.Value!.TotalLopDays.Should().Be(2m);
        summary.Value.Entries.Should().HaveCount(2);
        summary.Value.Entries.Should().OnlyContain(e => e.Source == LopSource.HrAssigned.ToString());
    }

    // ── FR-6 / BR-4: compulsory bulk assign end-to-end ─────────────

    [Fact]
    public async Task AssignCompulsory_EndToEnd_DeductsOrLopPerBalance()
    {
        // Ann has 5 days balance -> deduct; seed a second employee with no balance -> LOP.
        SeedBalance(_employeeA, _leaveTypeA, _tenantA, 5m);
        var emp2 = Guid.NewGuid();
        using (var db = DbFor(_tenantA))
        {
            db.Employees.Add(Emp(emp2, _tenantA, Guid.NewGuid(), "Cas"));
            db.SaveChanges();
        }

        var mediator = BuildPipeline(_tenantA, _hrAUser);

        var result = await mediator.Send(new AssignCompulsoryLeaveCommand(new CompulsoryLeaveRequest
        {
            LeaveTypeId = _leaveTypeA,
            Dates = [Day1],
            EmployeeIds = [_employeeA, emp2],
            Reason = "Company shutdown",
        }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeesProcessed.Should().Be(2);
        result.Value.AssignedCount.Should().Be(2);
        result.Value.LopCount.Should().Be(1);

        using var verify = DbFor(_tenantA);
        verify.LeaveRequests.Single(lr => lr.EmployeeId == _employeeA && !lr.IsLop && lr.StartDate == Day1)
            .Status.Should().Be(LeaveRequestStatus.Approved);
        verify.LeaveRequests.Single(lr => lr.EmployeeId == emp2 && lr.IsLop && lr.StartDate == Day1)
            .LopSource.Should().Be(LopSource.Compulsory);
        verify.CompulsoryLeaves.Count(c => c.Date == Day1 && c.LeaveTypeId == _leaveTypeA).Should().Be(1);
    }

    // ── NFR-2: tenant isolation ────────────────────────────────────

    [Fact]
    public async Task AssignLop_InTenantA_NotVisibleToTenantB()
    {
        var mediatorA = BuildPipeline(_tenantA, _hrAUser);
        await mediatorA.Send(new AssignLopCommand(new AssignLopRequest
        {
            EmployeeId = _employeeA,
            Dates = [Day1, Day2],
        }));

        // Tenant B summary for Tenant A's employee sees nothing (global query filter hides the rows).
        var mediatorB = BuildPipeline(_tenantB, _hrBUser);
        var summaryB = await mediatorB.Send(new GetLopSummaryQuery(_employeeA, Day1, Day3));

        summaryB.IsSuccess.Should().BeTrue();
        summaryB.Value!.TotalLopDays.Should().Be(0m);
        summaryB.Value.Entries.Should().BeEmpty();

        // And the rows really are scoped to Tenant A.
        using var dbA = DbFor(_tenantA);
        dbA.LeaveRequests.Count(lr => lr.EmployeeId == _employeeA && lr.IsLop).Should().Be(2);
        using var dbB = DbFor(_tenantB);
        dbB.LeaveRequests.Count(lr => lr.IsLop).Should().Be(0);
    }
}
