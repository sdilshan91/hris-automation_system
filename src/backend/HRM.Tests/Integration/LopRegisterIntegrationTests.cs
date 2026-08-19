// ============================================================================
// US-LV-011 (queue B2): cross-employee LOP register — GET /api/v1/leaves/lop-register.
//
// Sibling of the per-employee payroll lop-summary. These arms cover the query semantics that DON'T
// depend on real FK / soft-delete-INNER-JOIN behaviour and so run on the InMemory provider through the
// real composed pipeline (MediatR + ITenantContext global query filters):
//   - rows across MULTIPLE employees, each carrying the right EmployeeName / EmployeeNo
//   - the optional employeeIds filter narrows the result
//   - date-range boundaries: a LOP exactly on `from` and exactly on `to` are BOTH included
//   - empty range -> empty list (not an error)
//   - a soft-deleted leave request is excluded
//
// The tenant-isolation, soft-deleted-EMPLOYEE and Include-trap arms live in
// LopRegisterPostgresTests (real Postgres — InMemory enforces neither FKs nor the INNER-JOIN trap).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.Commands;
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

public sealed class LopRegisterIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _hrAUser = Guid.NewGuid();

    private readonly Guid _leaveTypeA = Guid.NewGuid();
    private readonly Guid _employee1 = Guid.NewGuid();
    private readonly Guid _employee2 = Guid.NewGuid();

    private static readonly DateOnly From = new(2026, 3, 2);  // Monday
    private static readonly DateOnly Mid = new(2026, 3, 3);
    private static readonly DateOnly To = new(2026, 3, 6);

    public LopRegisterIntegrationTests() => SeedData();

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
        services.AddScoped<ITenantLeaveYearResolver, TenantLeaveYearResolver>();
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
        using var db = DbFor(_tenantA);
        db.LeaveTypes.Add(new LeaveType
        {
            Id = _leaveTypeA, TenantId = _tenantA, Name = "Loss of Pay", Color = "#F44336",
            AnnualEntitlement = 0, AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, NegativeBalanceAllowed = false, IsActive = true,
        });
        db.Employees.AddRange(
            Emp(_employee1, _tenantA, "Ann", "Archer", "E-001"),
            Emp(_employee2, _tenantA, "Ben", "Boone", "E-002"));
        db.SaveChanges();
    }

    private static Employee Emp(Guid id, Guid tenantId, string first, string last, string no) => new()
    {
        Id = id, TenantId = tenantId, UserId = Guid.NewGuid(), EmployeeNo = no,
        FirstName = first, LastName = last, Email = $"{first}@t.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active, IsActive = true,
    };

    private LeaveRequest MakeLop(Guid empId, DateOnly date, LeaveRequestStatus status = LeaveRequestStatus.HrAssigned)
        => new()
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeId = empId, LeaveTypeId = _leaveTypeA,
            StartDate = date, EndDate = date, TotalDays = 1m, Status = status, RequestedAt = DateTime.UtcNow,
            IsLop = true, LopSource = LopSource.HrAssigned,
        };

    // Multi-day LOP spanning [start, end]. TotalDays is the register's `Days` column.
    private LeaveRequest MakeLopRange(Guid empId, DateOnly start, DateOnly end, decimal totalDays,
        LeaveRequestStatus status = LeaveRequestStatus.HrAssigned)
        => new()
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeId = empId, LeaveTypeId = _leaveTypeA,
            StartDate = start, EndDate = end, TotalDays = totalDays, Status = status, RequestedAt = DateTime.UtcNow,
            IsLop = true, LopSource = LopSource.HrAssigned,
        };

    // A normal (non-LOP) leave request inside the window — IsLop = false, so it must NEVER reach the register.
    private LeaveRequest MakeNonLop(Guid empId, DateOnly date, LeaveRequestStatus status = LeaveRequestStatus.Approved)
        => new()
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeId = empId, LeaveTypeId = _leaveTypeA,
            StartDate = date, EndDate = date, TotalDays = 1m, Status = status, RequestedAt = DateTime.UtcNow,
            IsLop = false, LopSource = null,
        };

    // ── multiple employees, each with the right identity ───────────
    [Fact]
    public async Task Register_ReturnsRowsAcrossEmployees_WithNameAndNumber()
    {
        using (var db = DbFor(_tenantA))
        {
            db.LeaveRequests.Add(MakeLop(_employee1, Mid));
            db.LeaveRequests.Add(MakeLop(_employee2, Mid));
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value.Should().ContainSingle(r => r.EmployeeId == _employee1
            && r.EmployeeName == "Ann Archer" && r.EmployeeNo == "E-001");
        result.Value.Should().ContainSingle(r => r.EmployeeId == _employee2
            && r.EmployeeName == "Ben Boone" && r.EmployeeNo == "E-002");
    }

    // ── employeeIds filter narrows the result ──────────────────────
    [Fact]
    public async Task Register_EmployeeIdsFilter_NarrowsToThoseEmployees()
    {
        using (var db = DbFor(_tenantA))
        {
            db.LeaveRequests.Add(MakeLop(_employee1, Mid));
            db.LeaveRequests.Add(MakeLop(_employee2, Mid));
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To, new[] { _employee1 }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle()
            .Which.EmployeeId.Should().Be(_employee1);
    }

    // ── boundaries: a LOP exactly on `from` and on `to` are included ─
    [Fact]
    public async Task Register_IncludesEntriesExactlyOnFromAndToBoundaries()
    {
        using (var db = DbFor(_tenantA))
        {
            db.LeaveRequests.Add(MakeLop(_employee1, From)); // exactly on `from`
            db.LeaveRequests.Add(MakeLop(_employee2, To));   // exactly on `to`
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(r => r.Date).Should().BeEquivalentTo(new[] { From, To });
    }

    // ── empty range -> empty list, not an error ────────────────────
    [Fact]
    public async Task Register_EmptyRange_ReturnsEmptyList_NotError()
    {
        // No LOP rows inside a range that precedes every seeded row.
        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    // ── a soft-deleted leave request is excluded ───────────────────
    [Fact]
    public async Task Register_ExcludesSoftDeletedLeaveRequest()
    {
        using (var db = DbFor(_tenantA))
        {
            var live = MakeLop(_employee1, Mid);
            var deleted = MakeLop(_employee2, Mid);
            deleted.IsDeleted = true;
            db.LeaveRequests.AddRange(live, deleted);
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle()
            .Which.EmployeeId.Should().Be(_employee1);
    }

    // ── gap 1: a non-LOP leave request inside the window is excluded ─
    // Guards the `lr.IsLop` clause. Every other seed is IsLop=true, so without this a normal approved
    // leave would silently pollute the register if the filter were dropped.
    [Fact]
    public async Task Register_ExcludesNonLopLeaveRequest_InsideWindow()
    {
        using (var db = DbFor(_tenantA))
        {
            db.LeaveRequests.Add(MakeLop(_employee1, Mid));          // effective LOP -> included
            db.LeaveRequests.Add(MakeNonLop(_employee2, Mid));       // normal approved leave -> excluded
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle()
            .Which.EmployeeId.Should().Be(_employee1);
    }

    // ── gap 2: Cancelled and Rejected LOPs inside the window are excluded ─
    // Guards the `Status != Cancelled && Status != Rejected` clause.
    [Fact]
    public async Task Register_ExcludesCancelledAndRejectedLops_InsideWindow()
    {
        using (var db = DbFor(_tenantA))
        {
            db.LeaveRequests.Add(MakeLop(_employee1, Mid, LeaveRequestStatus.Cancelled));
            db.LeaveRequests.Add(MakeLop(_employee2, Mid, LeaveRequestStatus.Rejected));
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    // ── gap 3: a multi-day LOP that SPANS a window boundary is included ─
    // Distinguishes the real overlap predicate (StartDate <= to && EndDate >= from) from a mutated
    // containment predicate (StartDate >= from && EndDate <= to): a single-day seed passes under both,
    // a boundary-spanning multi-day seed passes only under overlap.
    [Fact]
    public async Task Register_IncludesMultiDayLop_SpanningStartBoundary()
    {
        using (var db = DbFor(_tenantA))
        {
            // From = 2026-03-02. This LOP starts BEFORE the window and ends on `from`.
            // Overlap: 2026-02-28 <= 2026-03-06 && 2026-03-02 >= 2026-03-02 -> included.
            // Containment (mutation): 2026-02-28 >= 2026-03-02 is false -> would be dropped.
            db.LeaveRequests.Add(MakeLopRange(_employee1,
                new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 2), 3m));
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To));

        result.IsSuccess.Should().BeTrue();
        var row = result.Value!.Should().ContainSingle().Which;
        row.EmployeeId.Should().Be(_employee1);
        row.Date.Should().Be(new DateOnly(2026, 2, 28)); // Date == StartDate, even before the window
    }

    // ── gap 3 (mirror): a multi-day LOP spanning the FAR (`to`) boundary is included ─
    [Fact]
    public async Task Register_IncludesMultiDayLop_SpanningEndBoundary()
    {
        using (var db = DbFor(_tenantA))
        {
            // To = 2026-03-06. This LOP starts on `to` and ends AFTER the window.
            // Overlap: 2026-03-06 <= 2026-03-06 && 2026-03-08 >= 2026-03-02 -> included.
            // Containment (mutation): 2026-03-08 <= 2026-03-06 is false -> would be dropped.
            db.LeaveRequests.Add(MakeLopRange(_employee2,
                new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 8), 3m));
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To));

        result.IsSuccess.Should().BeTrue();
        var row = result.Value!.Should().ContainSingle().Which;
        row.EmployeeId.Should().Be(_employee2);
        row.Date.Should().Be(new DateOnly(2026, 3, 6));
    }

    // ── gap 4: a multi-day LOP emits ONE row with Days == TotalDays, Date == StartDate ─
    // Pins the documented one-row-per-request shape so a future switch to per-date expansion cannot
    // happen silently.
    [Fact]
    public async Task Register_MultiDayLop_YieldsSingleRow_WithDaysEqualTotalDays()
    {
        var start = new DateOnly(2026, 3, 3);
        using (var db = DbFor(_tenantA))
        {
            // 3-day LOP fully inside the window.
            db.LeaveRequests.Add(MakeLopRange(_employee1, start, new DateOnly(2026, 3, 5), 3m));
            db.SaveChanges();
        }

        var result = await BuildPipeline(_tenantA, _hrAUser)
            .Send(new GetLopRegisterQuery(From, To));

        result.IsSuccess.Should().BeTrue();
        var row = result.Value!.Should().ContainSingle().Which;
        row.Days.Should().Be(3m);
        row.Date.Should().Be(start);
        row.EmployeeId.Should().Be(_employee1);
    }
}
