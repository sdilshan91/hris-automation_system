// ============================================================================
// US-RPT-002: Leave & Attendance Reports — integration tests.
//
// Exercises the full MediatR pipeline (validator -> GenerateHrReportQueryHandler -> HrReportService),
// which for the leave reports REUSES the US-LV-012 LeaveReportService and for the attendance reports
// aggregates the US-ATT-007 attendance_monthly_summary materialized table. Covers the Test Hints:
//   - Leave Balance (BR-1): entitlement 20 - consumed 8 - pending 2 = balance 10 (+ colour band).
//   - Attendance Summary (FR-4): 18 present / 20 working -> attendance rate 90%.
//   - Overtime (BR-3): per-employee overtime hours from the materialized summary.
//   - Late arrival: late counts surfaced per employee / department.
//   - AC-5 tenant isolation: Tenant A's attendance-summary excludes Tenant B.
//   - AC-4 manager scope: a manager sees ONLY their direct reports (+ self), not the whole tenant.
//
// PROVIDER NOTE: InMemory, same rationale as the other integration tests (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker). Tenant isolation is proven by the EF global query filter;
// the ILeaveEntitlementService engine is substituted so balance math is exercised deterministically.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Application.Features.Reports.DTOs;
using HRM.Application.Features.Reports.Queries;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class HrLeaveAttendanceReportIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _hrAUser = Guid.NewGuid();
    private readonly Guid _managerUser = Guid.NewGuid();

    private Guid _leaveTypeA;
    private Guid _engDeptA;
    private Guid _salesDeptA;
    private Guid _deptB;

    private Guid _empAnn;     // Engineering, Tenant A, reports to nobody (the manager)
    private Guid _empArt;     // Engineering, Tenant A, reports to Ann
    private Guid _empCarol;   // Sales, Tenant A, reports to nobody
    private Guid _empBen;     // Tenant B

    private const int Year = 2026;
    private const string Month = "2026-06";

    public HrLeaveAttendanceReportIntegrationTests() => SeedData();

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

    private IMediator BuildPipeline(Guid tenantId, Guid userId, decimal entitlementDays, params string[] permissions)
        => BuildPipeline(tenantId, userId, entitlementDays, sharedCache: null, permissions);

    // Overload injecting a SHARED IDistributedCache so the cross-scope cache-key isolation (ISSUE-195 /
    // IsScopedReport, the anti-leak control) can be exercised — a manager and an HR caller with identical
    // filters must NEVER share a cached aggregate (BUG-116 class).
    private IMediator BuildPipeline(
        Guid tenantId, Guid userId, decimal entitlementDays, IDistributedCache? sharedCache, string[] permissions)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Permissions.Returns(permissions);

        var entitlement = Substitute.For<ILeaveEntitlementService>();
        entitlement
            .ComputeEffectiveEntitlementAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = ci.ArgAt<Guid>(0),
                LeaveTypeId = ci.ArgAt<Guid>(1),
                LeaveYear = ci.ArgAt<int>(2),
                ProratedEntitlementDays = entitlementDays,
            }));
        // ISSUE-287: LeaveReportService switched to the batch resolver in the BUG-124 (#258) N+1 fix;
        // the mock must stub it too or NSubstitute returns null → ArgumentNullException in the report.
        // Mirror the per-pair stub: a constant `entitlementDays` for every (employee × leave type) pair.
        entitlement
            .ComputeProratedEntitlementsBatchAsync(
                Arg.Any<IReadOnlyList<Employee>>(), Arg.Any<IReadOnlyList<LeaveType>>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var employees = ci.ArgAt<IReadOnlyList<Employee>>(0);
                var leaveTypes = ci.ArgAt<IReadOnlyList<LeaveType>>(1);
                var map = new Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal>();
                foreach (var emp in employees)
                    foreach (var lt in leaveTypes)
                        map[(emp.Id, lt.Id)] = entitlementDays;
                return map;
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton(entitlement);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IReportExportStorage, LocalReportExportStorage>();
        if (sharedCache is not null)
            services.AddSingleton(sharedCache); // exercises HrReportService._cache (the scoped cache-key path)
        services.AddScoped<ILeaveReportService, LeaveReportService>();
        services.AddScoped<IHrReportService, HrReportService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GenerateHrReportQuery).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private AppDbContext DbFor(Guid tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            new MutableTenantContext { TenantId = tenantId });

    private void SeedData()
    {
        _leaveTypeA = Guid.NewGuid();
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

        db.LeaveTypes.Add(new LeaveType
        {
            Id = _leaveTypeA, TenantId = _tenantA, Name = "Annual Leave", Code = "AL", Color = "#4CAF50",
            AnnualEntitlement = 20, AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, DisplayOrder = 1, IsActive = true,
        });
        db.LeaveTypes.Add(new LeaveType
        {
            Id = Guid.NewGuid(), TenantId = _tenantB, Name = "Annual Leave", Code = "AL", Color = "#4CAF50",
            AnnualEntitlement = 20, AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, DisplayOrder = 1, IsActive = true,
        });

        // Ann is the manager (Art reports to Ann). Ann/Carol report to nobody.
        db.Employees.AddRange(
            Emp(_empAnn, _tenantA, _engDeptA, "Ann", "A-ANN", userId: _managerUser),
            Emp(_empArt, _tenantA, _engDeptA, "Art", "A-ART", reportsTo: _empAnn),
            Emp(_empCarol, _tenantA, _salesDeptA, "Carol", "A-CAR"),
            Emp(_empBen, _tenantB, _deptB, "Ben", "B-BEN"));

        // ── Leave balance for Ann (BR-1 test hint): entitlement 20, used 8 (ledger), pending 2.
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeId = _empAnn, LeaveTypeId = _leaveTypeA,
            LeaveYear = Year, EntryType = LedgerEntryType.Used, Amount = -8m, BalanceAfter = 12m,
        });
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeId = _empAnn, LeaveTypeId = _leaveTypeA,
            StartDate = new DateOnly(Year, 6, 1), EndDate = new DateOnly(Year, 6, 2), TotalDays = 2m,
            Status = LeaveRequestStatus.Pending,
        });

        // ── Attendance monthly summary (US-ATT-007) for June 2026.
        // Ann: 18 present / 2 absent => 20 working => 90% attendance; 3 late; 120 OT minutes (2h).
        db.AttendanceMonthlySummaries.Add(Summary(_empAnn, present: 18, absent: 2, late: 3, early: 1, otMinutes: 120));
        // Art (direct report of Ann): 19 present / 1 absent; 1 late; 60 OT minutes (1h).
        db.AttendanceMonthlySummaries.Add(Summary(_empArt, present: 19, absent: 1, late: 1, early: 0, otMinutes: 60));
        // Carol (different manager line): 15 present / 5 absent; 5 late; 0 OT.
        db.AttendanceMonthlySummaries.Add(Summary(_empCarol, present: 15, absent: 5, late: 5, early: 2, otMinutes: 0));
        // Ben (Tenant B): must never appear in Tenant A reports.
        db.AttendanceMonthlySummaries.Add(SummaryFor(_tenantB, _empBen, present: 20, absent: 0, late: 9, early: 0, otMinutes: 600));

        db.SaveChanges();
    }

    private Employee Emp(Guid id, Guid tenantId, Guid deptId, string first, string empNo,
        Guid? userId = null, Guid? reportsTo = null) => new()
    {
        Id = id, TenantId = tenantId, UserId = userId, EmployeeNo = empNo,
        FirstName = first, LastName = "X", Email = $"{first}@t.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = deptId,
        JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active, IsActive = true, ReportsToEmployeeId = reportsTo,
    };

    private AttendanceMonthlySummary Summary(Guid empId, decimal present, decimal absent, int late, int early, int otMinutes)
        => SummaryFor(_tenantA, empId, present, absent, late, early, otMinutes);

    private static AttendanceMonthlySummary SummaryFor(
        Guid tenantId, Guid empId, decimal present, decimal absent, int late, int early, int otMinutes) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId, YearMonth = Month,
        TotalPresentDays = present, TotalAbsentDays = absent,
        TotalLateCount = late, TotalEarlyDepartureCount = early,
        TotalOvertimeMinutes = otMinutes, TotalWorkMinutes = (int)present * 480,
        GeneratedAt = DateTime.UtcNow,
    };

    private static HrReportQueryParams JuneWindow(Guid? employeeId = null) => new()
    {
        DateFrom = new DateTime(Year, 6, 1),
        DateTo = new DateTime(Year, 6, 30),
        EmployeeId = employeeId,
    };

    private static int Col(HrReportResult r, string header) =>
        r.Table.Columns.ToList().FindIndex(c => string.Equals(c, header, StringComparison.OrdinalIgnoreCase));

    // ── BR-1: leave balance entitlement 20 - used 8 - pending 2 = 10, with colour band ───────────

    [Fact]
    public async Task LeaveBalance_AppliesBr1Formula_AndColourBand()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m,
            PermissionCatalog.Leave.ViewAll, PermissionCatalog.Reports.View);

        var result = await mediator.Send(new GenerateHrReportQuery(
            "leave-balance", JuneWindow() with { LeaveTypeIds = [_leaveTypeA] }));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.Metadata.Type.Should().Be("leave-balance");

        int availCol = Col(report, "Available Balance");
        int empNoCol = Col(report, "Employee No");
        int pctCol = Col(report, "Remaining %");

        // AC-2: the band is carried in cellBands (FE contract), NOT a "Band" text column.
        report.Table.Columns.Should().NotContain("Band");
        report.Table.CellBands.Should().NotBeNull();
        // cellBands is aligned to rows by [rowIndex][colIndex] — same row count, each inner array sized to columns.
        report.Table.CellBands!.Should().HaveCount(report.Table.Rows.Count);
        report.Table.CellBands.Should().OnlyContain(b => b.Count == report.Table.Columns.Count);

        int annIdx = report.Table.Rows.ToList().FindIndex(r => (string?)r[empNoCol] == "A-ANN");
        annIdx.Should().BeGreaterThanOrEqualTo(0);
        var annRow = report.Table.Rows[annIdx];
        // BR-1 current balance = entitlement 20 - used 8 - pending 2 = 10.
        ((decimal)annRow[availCol]!).Should().Be(10m);
        // Remaining % = 10 / available (entitlement 20 + carry 0) = 50% -> "warn" band (25-50, inclusive at 50).
        ((decimal)annRow[pctCol]!).Should().Be(50m);
        // The band sits on the "Available Balance" cell; every other cell in the row is null (rendered plainly).
        report.Table.CellBands[annIdx][availCol].Should().Be("warn");
        report.Table.CellBands[annIdx][empNoCol].Should().BeNull();
    }

    // ── FR-4: attendance rate = 18 / 20 * 100 = 90% (Ann, single employee filter) ───────────────

    [Fact]
    public async Task AttendanceSummary_Calculates90PercentRate_For18Of20Present()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m,
            PermissionCatalog.Attendance.ViewAll, PermissionCatalog.Reports.View);

        var result = await mediator.Send(new GenerateHrReportQuery(
            "attendance-summary", JuneWindow(employeeId: _empAnn)));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.Metadata.Type.Should().Be("attendance-summary");

        // 18 present / 20 working => 90% attendance, 10% absenteeism.
        report.Metadata.Summary.Single(s => s.Label == "Average Attendance %").Value.Should().Be(90m);
        report.Metadata.Summary.Single(s => s.Label == "Absenteeism Rate %").Value.Should().Be(10m);
        report.Metadata.Summary.Single(s => s.Label == "Late Arrivals").Value.Should().Be(3);
        // 120 OT minutes => 2 hours.
        report.Metadata.Summary.Single(s => s.Label == "Overtime Hours").Value.Should().Be(2m);
    }

    // ── BR-3: overtime hours from the materialized summary ──────────────────────────────────────

    [Fact]
    public async Task OvertimeReport_SurfacesPerEmployeeExcessHours()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m,
            PermissionCatalog.Attendance.ViewAll, PermissionCatalog.Reports.View);

        var result = await mediator.Send(new GenerateHrReportQuery("overtime-report", JuneWindow()));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;

        int hoursCol = Col(report, "Overtime Hours");
        int empNoCol = Col(report, "Employee No");

        // Ann 120m=2h, Art 60m=1h; Carol has 0 OT and is excluded. Total 3h.
        report.Metadata.Summary.Single(s => s.Label == "Total Overtime Hours").Value.Should().Be(3m);
        report.Table.Rows.Should().HaveCount(2);
        ((decimal)report.Table.Rows.Single(r => (string?)r[empNoCol] == "A-ANN")[hoursCol]!).Should().Be(2m);
        ((decimal)report.Table.Rows.Single(r => (string?)r[empNoCol] == "A-ART")[hoursCol]!).Should().Be(1m);
    }

    // ── late-arrival flagging ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LateArrivalReport_RanksEmployeesByLateCount()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m,
            PermissionCatalog.Attendance.ViewAll, PermissionCatalog.Reports.View);

        var result = await mediator.Send(new GenerateHrReportQuery("late-arrival-report", JuneWindow()));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;

        int lateCol = Col(report, "Late Count");
        int empNoCol = Col(report, "Employee No");

        // Ann 3 + Art 1 + Carol 5 = 9 total late.
        report.Metadata.Summary.Single(s => s.Label == "Total Late Arrivals").Value.Should().Be(9);
        ((int)report.Table.Rows.Single(r => (string?)r[empNoCol] == "A-CAR")[lateCol]!).Should().Be(5);
        // Highest-first ordering: Carol (5) leads.
        ((string?)report.Table.Rows[0][empNoCol]).Should().Be("A-CAR");
    }

    // ── AC-5: tenant isolation on a new report type ─────────────────────────────────────────────

    [Fact]
    public async Task AttendanceSummary_TenantA_ExcludesTenantB()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m,
            PermissionCatalog.Attendance.ViewAll, PermissionCatalog.Reports.View);

        var result = await mediator.Send(new GenerateHrReportQuery("late-arrival-report", JuneWindow()));

        result.IsSuccess.Should().BeTrue();
        // Ben (Tenant B) has 9 late — if leaked he would dominate. Tenant A total is 3+1+5 = 9 WITHOUT Ben.
        var report = result.Value!;
        report.Metadata.Summary.Single(s => s.Label == "Total Late Arrivals").Value.Should().Be(9);
        int empNoCol = Col(report, "Employee No");
        report.Table.Rows.Should().NotContain(r => (string?)r[empNoCol] == "B-BEN");
    }

    // ── AC-4: manager scope — Ann sees only her direct reports (Art) + herself, not Carol ───────

    [Fact]
    public async Task AttendanceSummary_ManagerScope_SeesOnlyDirectReportsAndSelf()
    {
        // Ann (manager) has Reports.View + Attendance.View.Team (NO .View.All) -> Manager scope.
        var mediator = BuildPipeline(_tenantA, _managerUser, entitlementDays: 20m,
            PermissionCatalog.Reports.View, PermissionCatalog.Attendance.ViewTeam);

        var result = await mediator.Send(new GenerateHrReportQuery("late-arrival-report", JuneWindow()));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        int empNoCol = Col(report, "Employee No");

        var seen = report.Table.Rows.Select(r => (string?)r[empNoCol]).ToList();
        // Manager scope: Ann (self) + Art (direct report). Carol (other line) and Ben (Tenant B) excluded.
        seen.Should().Contain("A-ANN");
        seen.Should().Contain("A-ART");
        seen.Should().NotContain("A-CAR");
        seen.Should().NotContain("B-BEN");
        // Late total = Ann 3 + Art 1 = 4 (Carol's 5 excluded by scope).
        report.Metadata.Summary.Single(s => s.Label == "Total Late Arrivals").Value.Should().Be(4);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-195 — the US-RPT-001 HR-employee report builders (headcount / turnover / demographics /
    //  joiners-leavers / department-distribution / employment-type-breakdown) must apply the SAME BR-2 role
    //  scope the leave/attendance reports already do: a Manager sees ONLY their direct reports (+ self) and
    //  the emitted Scope reads "Manager"; HR (holding an .View.All permission) sees the whole tenant as "All".
    //  Before the fix these builders always aggregated the entire workforce and emitted no scope.
    // ════════════════════════════════════════════════════════════════════════

    // ── ISSUE-195: headcount — manager sees only direct reports (Ann + Art), Scope='Manager' ────
    [Fact]
    public async Task Headcount_ManagerScope_CountsOnlyDirectReportsAndSelf()
    {
        // Ann (manager) holds Reports.View + Employee.View.Team (NO .View.All) -> Manager scope.
        var mediator = BuildPipeline(_tenantA, _managerUser, entitlementDays: 20m,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewTeam);

        var result = await mediator.Send(new GenerateHrReportQuery("headcount", JuneWindow()));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        // Manager scope: Ann (self) + Art (direct report) = 2. Carol (other line) and Ben (Tenant B) excluded.
        report.Metadata.Summary.Single(s => s.Label == "Total Headcount").Value.Should().Be(2);
        report.Metadata.Summary.Single(s => s.Label == "Scope").Value.Should().Be("Manager");
    }

    // ── ISSUE-195: headcount — HR (View.All) sees the whole tenant (3), Scope='All' ─────────────
    [Fact]
    public async Task Headcount_HrScope_CountsWholeTenant_AsAll()
    {
        // HR holds Employee.View.All -> All scope (whole tenant).
        var mediator = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewAll);

        var result = await mediator.Send(new GenerateHrReportQuery("headcount", JuneWindow()));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        // All scope: Ann + Art + Carol = 3 (Tenant B's Ben still excluded by the tenant filter).
        report.Metadata.Summary.Single(s => s.Label == "Total Headcount").Value.Should().Be(3);
        report.Metadata.Summary.Single(s => s.Label == "Scope").Value.Should().Be("All");
    }

    // ── ISSUE-195: department-distribution — second builder, manager scoped to their team ───────
    [Fact]
    public async Task DepartmentDistribution_ManagerScope_ShowsOnlyManagersDepartment()
    {
        var managerMediator = BuildPipeline(_tenantA, _managerUser, entitlementDays: 20m,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewTeam);
        var hrMediator = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewAll);

        var managerResult = await managerMediator.Send(new GenerateHrReportQuery("department-distribution", JuneWindow()));
        var hrResult = await hrMediator.Send(new GenerateHrReportQuery("department-distribution", JuneWindow()));

        managerResult.IsSuccess.Should().BeTrue();
        hrResult.IsSuccess.Should().BeTrue();

        // Manager: only Ann + Art (both Engineering) = 2 active, Engineering only.
        var manager = managerResult.Value!;
        manager.Metadata.Summary.Single(s => s.Label == "Total Active Headcount").Value.Should().Be(2);
        manager.Metadata.Summary.Single(s => s.Label == "Scope").Value.Should().Be("Manager");
        manager.Table.Rows.Should().OnlyContain(r => (string?)r[0] == "Engineering");

        // HR: whole tenant — Engineering (Ann+Art) + Sales (Carol) = 3 active across both departments.
        var hr = hrResult.Value!;
        hr.Metadata.Summary.Single(s => s.Label == "Total Active Headcount").Value.Should().Be(3);
        hr.Metadata.Summary.Single(s => s.Label == "Scope").Value.Should().Be("All");
        hr.Table.Rows.Select(r => (string?)r[0]).Should().Contain(new[] { "Engineering", "Sales" });
    }

    // ── ISSUE-195: cache-key folds the caller's scope — a Manager must NOT be served HR's cached
    //    full-tenant aggregate (guards IsScopedReport; a cross-scope cache leak is the BUG-116 class) ──
    [Fact]
    public async Task HrReport_CacheKey_FoldsScope_ManagerNotServedHrsCachedAllAggregate()
    {
        // ONE shared cache across both callers. Same tenant, same report, same filters — the ONLY thing that
        // may keep their answers apart is the scope segment folded into the cache key by IsScopedReport.
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        // HR runs first and CACHES the full-tenant headcount (3, Scope='All').
        var hr = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m, cache,
            new[] { PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewAll });
        var hrResult = await hr.Send(new GenerateHrReportQuery("headcount", JuneWindow()));
        hrResult.IsSuccess.Should().BeTrue();
        hrResult.Value!.Metadata.Summary.Single(s => s.Label == "Total Headcount").Value.Should().Be(3);

        // The Manager requests the SAME report+filters on the SAME cache. If the key did not fold scope it would
        // hit HR's cached {3, All}; instead it must compute its OWN scoped {2, Manager}.
        var mgr = BuildPipeline(_tenantA, _managerUser, entitlementDays: 20m, cache,
            new[] { PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewTeam });
        var mgrResult = await mgr.Send(new GenerateHrReportQuery("headcount", JuneWindow()));

        mgrResult.IsSuccess.Should().BeTrue();
        var mgrReport = mgrResult.Value!;
        mgrReport.Metadata.Summary.Single(s => s.Label == "Total Headcount").Value
            .Should().Be(2, "the manager must get its own team-scoped count, NOT HR's cached full-tenant aggregate");
        mgrReport.Metadata.Summary.Single(s => s.Label == "Scope").Value.Should().Be("Manager");
    }

    // ── AC-1: leave-utilization reuses the US-LV-012 utilization aggregation ─────────────────────

    [Fact]
    public async Task LeaveUtilization_ReturnsTenantScopedUtilization()
    {
        var mediator = BuildPipeline(_tenantA, _hrAUser, entitlementDays: 20m,
            PermissionCatalog.Leave.ViewAll, PermissionCatalog.Reports.View);

        var result = await mediator.Send(new GenerateHrReportQuery(
            "leave-utilization", JuneWindow() with { LeaveTypeIds = [_leaveTypeA] }));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.Metadata.Type.Should().Be("leave-utilization");
        report.Charts.Should().Contain(c => c.Title == "Leave Type Distribution");
        report.Metadata.Summary.Should().Contain(s => s.Label == "Average Utilization %");
    }
}
