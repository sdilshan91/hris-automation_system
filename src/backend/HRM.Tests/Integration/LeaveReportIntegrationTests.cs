// ============================================================================
// US-LV-012: Leave Reports & Analytics — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (MediatR pipeline + ITenantContext-driven global query filters),
// covering:
//   - GET /api/v1/leaves/reports/{reportType} end-to-end with a department filter + pagination.
//   - GET /api/v1/leaves/reports/{reportType}/export returns a CSV file inline for a small dataset (AC-5).
//   - Tenant isolation: HR in Tenant A sees NO Tenant B rows in a report (Test Hint, BR-1/NFR-3).
//
// PROVIDER NOTE: same rationale as LopIntegrationTests / TeamLeaveCalendarIntegrationTests — the verify
// gate runs `dotnet test` with no PostgreSQL bound, so these use the InMemory provider but go through the
// real composed pipeline (MediatR + query filters), which is what proves tenant isolation.
//
// The US-LV-002 entitlement engine is substituted (NSubstitute) so the report aggregation is exercised
// independently of entitlement resolution (which has its own tests); everything else is the real graph.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Application.Features.LeaveReports.DTOs;
using HRM.Application.Features.LeaveReports.Queries;
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

public sealed class LeaveReportIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _hrAUser = Guid.NewGuid();
    private readonly Guid _hrBUser = Guid.NewGuid();

    private Guid _leaveTypeA;
    private Guid _leaveTypeB;
    private Guid _engDeptA;
    private Guid _salesDeptA;
    private Guid _deptB;
    private Guid _empAnn;    // Engineering, Tenant A
    private Guid _empArt;    // Engineering, Tenant A
    private Guid _empCarol;  // Sales, Tenant A
    private Guid _empBen;    // Tenant B

    private const int Year = 2026;

    public LeaveReportIntegrationTests()
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

        // Entitlement engine: every (employee, leaveType) resolves to the leave type's annual entitlement.
        var entitlement = Substitute.For<ILeaveEntitlementService>();
        entitlement
            .ComputeEffectiveEntitlementAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = ci.ArgAt<Guid>(0),
                LeaveTypeId = ci.ArgAt<Guid>(1),
                LeaveYear = ci.ArgAt<int>(2),
                ProratedEntitlementDays = 14m,
            }));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton(entitlement);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IReportExportStorage, LocalReportExportStorage>();
        services.AddScoped<ILeaveReportService, LeaveReportService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetLeaveReportQuery).Assembly));

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
        _engDeptA = Guid.NewGuid();
        _salesDeptA = Guid.NewGuid();
        _deptB = Guid.NewGuid();
        _empAnn = Guid.NewGuid();
        _empArt = Guid.NewGuid();
        _empCarol = Guid.NewGuid();
        _empBen = Guid.NewGuid();

        using var db = DbFor(_tenantA);

        db.Departments.AddRange(
            new Department { Id = _engDeptA, TenantId = _tenantA, Name = "Engineering", Code = "ENG" },
            new Department { Id = _salesDeptA, TenantId = _tenantA, Name = "Sales", Code = "SAL" },
            new Department { Id = _deptB, TenantId = _tenantB, Name = "Ops", Code = "OPS" });

        db.LeaveTypes.AddRange(
            MakeLeaveType(_leaveTypeA, _tenantA),
            MakeLeaveType(_leaveTypeB, _tenantB));

        db.Employees.AddRange(
            Emp(_empAnn, _tenantA, _engDeptA, "Ann", "A-ANN"),
            Emp(_empArt, _tenantA, _engDeptA, "Art", "A-ART"),
            Emp(_empCarol, _tenantA, _salesDeptA, "Carol", "A-CAR"),
            Emp(_empBen, _tenantB, _deptB, "Ben", "B-BEN"));

        db.SaveChanges();
    }

    private static LeaveType MakeLeaveType(Guid id, Guid tenantId) => new()
    {
        Id = id, TenantId = tenantId, Name = "Annual Leave", Code = "AL", Color = "#4CAF50",
        AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront,
        Gender = LeaveTypeGender.All, DisplayOrder = 1, IsActive = true,
    };

    private static Employee Emp(Guid id, Guid tenantId, Guid deptId, string first, string empNo) => new()
    {
        Id = id, TenantId = tenantId, UserId = null, EmployeeNo = empNo,
        FirstName = first, LastName = "X", Email = $"{first}@t.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = deptId,
        JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active, IsActive = true,
    };

    private static LeaveReportQueryParams Params(
        Guid? departmentId = null, Guid? leaveTypeId = null, int page = 1, int pageSize = 50) => new()
    {
        DepartmentId = departmentId, LeaveTypeId = leaveTypeId, Year = Year,
        Page = page, PageSize = pageSize,
    };

    private static int ColumnIndex(LeaveReportResult r, string header)
        => r.Columns.ToList().FindIndex(c => string.Equals(c, header, StringComparison.OrdinalIgnoreCase));

    private static string Cell(LeaveReportRow row, LeaveReportResult r, string header)
        => row.Cells[ColumnIndex(r, header)];

    // ── GET reports/{reportType} end-to-end with filter + pagination ──

    [Fact]
    public async Task GetReport_EndToEnd_WithDepartmentFilterAndPagination()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser, PermissionCatalog.Leave.ViewAll);

        // Page 1, size 1 over the 2 Engineering employees -> 1 row, total count 2.
        var result = await mediator.Send(new GetLeaveReportQuery(
            LeaveReportType.BalanceSummary,
            Params(departmentId: _engDeptA, leaveTypeId: _leaveTypeA, page: 1, pageSize: 1)));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.Scope.Should().Be("All");
        report.TotalCount.Should().Be(2);          // Ann + Art (Engineering)
        report.Rows.Should().HaveCount(1);          // pageSize 1
        report.Rows.Should().OnlyContain(r => Cell(r, report, "Department") == "Engineering");
    }

    // ── export endpoint returns a CSV file inline for a small dataset (AC-5) ──

    [Fact]
    public async Task ExportReport_EndToEnd_ReturnsCsvFileInline()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser, PermissionCatalog.Leave.ViewAll);

        var result = await mediator.Send(new ExportLeaveReportQuery(
            LeaveReportType.BalanceSummary, ReportExportFormat.Csv,
            Params(leaveTypeId: _leaveTypeA)));

        result.IsSuccess.Should().BeTrue();
        var export = result.Value!;
        export.Queued.Should().BeFalse();
        export.ContentType.Should().Be("text/csv");
        export.FileName.Should().EndWith(".csv");
        export.FileContent.Should().NotBeNull();

        var text = Encoding.UTF8.GetString(export.FileContent!);
        text.Should().Contain("Employee No").And.Contain("Balance");
        // The 3 Tenant A employees each produce an annual row; no Tenant B leakage.
        text.Should().Contain("A-ANN").And.Contain("A-ART").And.Contain("A-CAR");
        text.Should().NotContain("B-BEN");
    }

    // ── tenant isolation: HR in Tenant A sees NO Tenant B rows (BR-1 / NFR-3) ──

    [Fact]
    public async Task GetReport_TenantIsolation_HrInTenantA_SeesNoTenantBRows()
    {
        var mediatorA = BuildPipeline(_tenantA, _hrAUser, PermissionCatalog.Leave.ViewAll);

        var result = await mediatorA.Send(new GetLeaveReportQuery(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _leaveTypeA)));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;

        // Only the three Tenant A employees; Ben (Tenant B) must be invisible.
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "A-ANN", "A-ART", "A-CAR" });
        report.Rows.Should().NotContain(r => Cell(r, report, "Employee No") == "B-BEN");

        // And Tenant B HR, querying the same report, sees ONLY Ben.
        var mediatorB = BuildPipeline(_tenantB, _hrBUser, PermissionCatalog.Leave.ViewAll);
        var resultB = await mediatorB.Send(new GetLeaveReportQuery(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _leaveTypeB)));

        resultB.Value!.Rows.Select(r => Cell(r, resultB.Value!, "Employee No"))
            .Should().BeEquivalentTo(new[] { "B-BEN" });
    }
}
