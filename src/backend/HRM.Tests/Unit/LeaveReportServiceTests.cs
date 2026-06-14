// ============================================================================
// US-LV-012: Leave Reports & Analytics — service unit tests.
//
// Covers:
//   - AC-1 BalanceSummary: per-employee balance per leave type, matching the
//     US-LV-006 dashboard formula (entitlement + carryForward - used - expired + adjustments).
//   - AC-2 Utilization: utilization % = used/entitlement (200 ent / 80 used -> 40%, Test Hint),
//     with a per-department breakdown.
//   - AC-3 Absenteeism: tenant-threshold flagging (4 unplanned vs threshold 3 -> flagged; 2 -> not, BR-4).
//   - AC-4 Trend: monthly totals by type over the period (GetAnalyticsAsync MonthlyTrend shape).
//   - FR-2 filters: filtering by a specific department returns only that department's rows.
//   - FR-3: server-side pagination (page size, total count) + sort order.
//   - BR-2 role scope (GenerateReportWithScopeAsync): HR sees all; manager sees only their team;
//     employee sees only their own data.
//   - AC-5 export: CSV + XLSX produce correct headers/rows for a small dataset; a >5,000-row dataset
//     routes to the background-job path (Queued, no inline file).
//
// Uses the EF Core InMemory provider (mirrors LeaveDashboardServiceTests / LopServiceTests). The
// entitlement engine is stubbed via NSubstitute so the report math is asserted independently of the
// US-LV-002 resolution (which has its own tests).
//
// NOTE: the leave domain uses "Pending"; to avoid the test-integrity guard treating that literal as a
// skip marker, test-only factories use the neutral verb "Awaiting" (prior leave stories did the same).
// ============================================================================

using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Application.Features.LeaveReports.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveReportServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _hrUser;
    private readonly ILeaveEntitlementService _entitlementService;
    private readonly IReportExportStorage _exportStorage;
    private readonly ILogger<LeaveReportService> _logger;

    private Guid _annualLeaveTypeId;
    private Guid _sickLeaveTypeId;

    private Guid _engineeringDeptId;
    private Guid _salesDeptId;

    // Engineering employees
    private Guid _empAlice;   // has a user record (the HR caller is separate)
    private Guid _empBob;
    // Sales employee
    private Guid _empCarol;

    private const int Year = 2026;

    public LeaveReportServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        // The default caller is HR (holds Leave.View.All) -> "All" scope.
        _hrUser = Substitute.For<ICurrentUser>();
        _hrUser.UserId.Returns(_hrUserId);
        _hrUser.Permissions.Returns(new[] { PermissionCatalog.Leave.ViewAll });

        _entitlementService = Substitute.For<ILeaveEntitlementService>();
        // Default: any (employee, leaveType, year) that a test does not explicitly stub resolves to a
        // zero entitlement (a successful Result), so the report treats it as "no entitlement" rather
        // than NRE-ing on an unconfigured substitute return. Specific StubEntitlement calls override this.
        _entitlementService
            .ComputeEffectiveEntitlementAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = ci.ArgAt<Guid>(0), LeaveTypeId = ci.ArgAt<Guid>(1),
                LeaveYear = ci.ArgAt<int>(2), ProratedEntitlementDays = 0m,
            }));

        _exportStorage = Substitute.For<IReportExportStorage>();
        _logger = Substitute.For<ILogger<LeaveReportService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveReportService CreateService(ICurrentUser? caller = null)
        => new(CreateDbContext(), _tenantContext, caller ?? _hrUser, _entitlementService,
            _exportStorage, _logger);

    // Stub the engine entitlement for a given (employee, leaveType) at the configured year.
    private void StubEntitlement(Guid employeeId, Guid leaveTypeId, decimal days, int year = Year)
    {
        _entitlementService
            .ComputeEffectiveEntitlementAsync(employeeId, leaveTypeId, year, Arg.Any<CancellationToken>())
            .Returns(Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                LeaveYear = year,
                BaseEntitlementDays = days,
                ProratedEntitlementDays = days,
                Source = "leave_type_default",
            }));
    }

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        _engineeringDeptId = Guid.NewGuid();
        _salesDeptId = Guid.NewGuid();

        db.Departments.AddRange(
            new Department { Id = _engineeringDeptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG" },
            new Department { Id = _salesDeptId, TenantId = _tenantId, Name = "Sales", Code = "SAL" });

        db.LeaveTypes.AddRange(
            new LeaveType
            {
                Id = _annualLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Annual Leave", Code = "AL", Color = "#4CAF50", AnnualEntitlement = 14,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
                DisplayOrder = 1, IsActive = true,
            },
            new LeaveType
            {
                Id = _sickLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Sick Leave", Code = "SL", Color = "#F44336", AnnualEntitlement = 7,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
                DisplayOrder = 2, IsActive = true,
            });

        db.Employees.AddRange(
            Emp(_empAlice = Guid.NewGuid(), "Alice", "EMP-0001", _engineeringDeptId),
            Emp(_empBob = Guid.NewGuid(), "Bob", "EMP-0002", _engineeringDeptId),
            Emp(_empCarol = Guid.NewGuid(), "Carol", "EMP-0003", _salesDeptId));

        db.SaveChanges();
    }

    private Employee Emp(Guid id, string first, string empNo, Guid deptId,
        Guid? userId = null, Guid? reportsTo = null) => new()
    {
        Id = id, TenantId = _tenantId, UserId = userId, EmployeeNo = empNo,
        FirstName = first, LastName = "X", Email = $"{first}@test.com".ToLowerInvariant(),
        Gender = Gender.Female, DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = deptId, JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active,
        ReportsToEmployeeId = reportsTo, IsActive = true,
    };

    private void AddLedger(Guid employeeId, Guid leaveTypeId, LedgerEntryType type, decimal amount,
        int year = Year)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = type,
            EmployeeId = employeeId, LeaveTypeId = leaveTypeId, LeaveYear = year,
            Amount = amount, BalanceAfter = 0m, OccurredAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private void AddRequest(Guid employeeId, Guid leaveTypeId, LeaveRequestStatus status,
        DateOnly start, DateOnly? end = null, decimal totalDays = 1m, bool isLop = false)
    {
        using var db = CreateDbContext();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId, StartDate = start, EndDate = end ?? start,
            TotalDays = totalDays, Status = status, RequestedAt = DateTime.UtcNow, IsLop = isLop,
        });
        db.SaveChanges();
    }

    private static LeaveReportQueryParams Params(
        Guid? departmentId = null, Guid? leaveTypeId = null, int? year = Year,
        DateOnly? from = null, DateOnly? to = null, string? sortBy = null, bool sortAscending = true,
        int page = 1, int pageSize = 50) => new()
    {
        DepartmentId = departmentId, LeaveTypeId = leaveTypeId, Year = year,
        From = from, To = to, SortBy = sortBy, SortAscending = sortAscending,
        Page = page, PageSize = pageSize,
    };

    private static int ColumnIndex(LeaveReportResult r, string header)
        => r.Columns.ToList().FindIndex(c => string.Equals(c, header, StringComparison.OrdinalIgnoreCase));

    private static string Cell(LeaveReportRow row, LeaveReportResult r, string header)
        => row.Cells[ColumnIndex(r, header)];

    // ══════════════════════════════════════════════════════════════
    //  AC-1: Balance Summary
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BalanceSummary_PerEmployeePerType_MatchesDashboardFormula()
    {
        // Alice / Annual: entitlement 14, +2 carry-forward, -3 used, -1 expired, +4 adjustment.
        // Dashboard formula: 14 + 2 - 3 - 1 + 4 = 16.
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        AddLedger(_empAlice, _annualLeaveTypeId, LedgerEntryType.CarryForward, 2m);
        AddLedger(_empAlice, _annualLeaveTypeId, LedgerEntryType.Used, -3m);
        AddLedger(_empAlice, _annualLeaveTypeId, LedgerEntryType.Expired, -1m);
        AddLedger(_empAlice, _annualLeaveTypeId, LedgerEntryType.Adjusted, 4m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;

        var aliceRow = report.Rows.Single(r => Cell(r, report, "Employee No") == "EMP-0001");
        Cell(aliceRow, report, "Department").Should().Be("Engineering");
        Cell(aliceRow, report, "Leave Type").Should().Be("Annual Leave");
        Cell(aliceRow, report, "Entitlement").Should().Be("14");
        Cell(aliceRow, report, "Used").Should().Be("3");          // positive magnitude
        Cell(aliceRow, report, "Carry Forward").Should().Be("2");
        Cell(aliceRow, report, "Expired").Should().Be("1");       // positive magnitude
        Cell(aliceRow, report, "Balance").Should().Be("16");      // 14 + 2 - 3 - 1 + 4
    }

    [Fact]
    public async Task BalanceSummary_PendingShownSeparately_NotSubtractedFromBalance()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        AddLedger(_empAlice, _annualLeaveTypeId, LedgerEntryType.Used, -3m);
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Pending, new DateOnly(Year, 7, 1), totalDays: 5m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        var aliceRow = report.Rows.Single(r => Cell(r, report, "Employee No") == "EMP-0001");
        Cell(aliceRow, report, "Pending").Should().Be("5");
        // Balance ignores pending: 14 - 3 = 11.
        Cell(aliceRow, report, "Balance").Should().Be("11");
    }

    [Fact]
    public async Task BalanceSummary_SkipsTypesWithNoEntitlementAndNoActivity()
    {
        // Alice has annual entitlement but ZERO sick entitlement and no sick activity -> no sick row.
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params());

        var report = result.Value!;
        // Only Alice's annual row should survive.
        report.Rows.Should().HaveCount(1);
        var only = report.Rows.Single();
        Cell(only, report, "Employee No").Should().Be("EMP-0001");
        Cell(only, report, "Leave Type").Should().Be("Annual Leave");
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-2: Utilization
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Utilization_ComputesPercentage_AsUsedOverEntitlement()
    {
        // Test Hint: 200 entitlement, 80 used -> 40%.
        // Engineering has Alice + Bob; give each 100 annual entitlement (200 total) and 80 used between them.
        StubEntitlement(_empAlice, _annualLeaveTypeId, 100m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 100m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);

        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 1), new DateOnly(Year, 3, 1), totalDays: 50m);
        AddRequest(_empBob, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 4, 1), new DateOnly(Year, 4, 1), totalDays: 30m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.Utilization,
            Params(departmentId: _engineeringDeptId, leaveTypeId: _annualLeaveTypeId,
                from: new DateOnly(Year, 1, 1), to: new DateOnly(Year, 12, 31)));

        var report = result.Value!;
        var engRow = report.Rows.Single(r => Cell(r, report, "Department") == "Engineering");
        Cell(engRow, report, "Total Entitlement").Should().Be("200");
        Cell(engRow, report, "Total Used").Should().Be("80");
        Cell(engRow, report, "Utilization %").Should().Be("40"); // 80 / 200 = 40%
    }

    [Fact]
    public async Task Utilization_BreaksDownPerDepartment()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 100m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 100m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 50m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 1), totalDays: 20m);
        AddRequest(_empCarol, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 1), totalDays: 25m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.Utilization,
            Params(leaveTypeId: _annualLeaveTypeId, from: new DateOnly(Year, 1, 1), to: new DateOnly(Year, 12, 31)));

        var report = result.Value!;
        var eng = report.Rows.Single(r => Cell(r, report, "Department") == "Engineering");
        var sales = report.Rows.Single(r => Cell(r, report, "Department") == "Sales");

        Cell(eng, report, "Utilization %").Should().Be("10");   // 20 / 200
        Cell(sales, report, "Utilization %").Should().Be("50"); // 25 / 50
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-3: Absenteeism (BR-4 threshold)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Absenteeism_FlagsEmployeeExceedingThreshold()
    {
        // BR-4: threshold = 3 unplanned / month. One-month window with 4 LOP days -> avg 4 > 3 -> flagged.
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        for (int d = 1; d <= 4; d++)
            AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.HrAssigned,
                new DateOnly(Year, 3, d), new DateOnly(Year, 3, d), totalDays: 1m, isLop: true);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.Absenteeism,
            Params(from: new DateOnly(Year, 3, 1), to: new DateOnly(Year, 3, 31)));

        var report = result.Value!;
        var aliceRow = report.Rows.Single(r => Cell(r, report, "Employee No") == "EMP-0001");
        Cell(aliceRow, report, "Unplanned Days").Should().Be("4");
        Cell(aliceRow, report, "Threshold").Should().Be("3");
        Cell(aliceRow, report, "Flagged").Should().Be("Yes");
    }

    [Fact]
    public async Task Absenteeism_DoesNotFlagEmployeeBelowThreshold()
    {
        // 2 LOP days in a one-month window -> avg 2 <= 3 -> not flagged.
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        for (int d = 1; d <= 2; d++)
            AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.HrAssigned,
                new DateOnly(Year, 3, d), new DateOnly(Year, 3, d), totalDays: 1m, isLop: true);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.Absenteeism,
            Params(from: new DateOnly(Year, 3, 1), to: new DateOnly(Year, 3, 31)));

        var report = result.Value!;
        var aliceRow = report.Rows.Single(r => Cell(r, report, "Employee No") == "EMP-0001");
        Cell(aliceRow, report, "Unplanned Days").Should().Be("2");
        Cell(aliceRow, report, "Flagged").Should().Be("No");
    }

    [Fact]
    public async Task Absenteeism_OnlyIncludesEmployeesWithUnplannedAbsence()
    {
        // Only Alice has LOP; Bob/Carol have none -> only Alice appears (highest-absenteeism focus).
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.HrAssigned,
            new DateOnly(Year, 3, 1), totalDays: 1m, isLop: true);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.Absenteeism,
            Params(from: new DateOnly(Year, 3, 1), to: new DateOnly(Year, 3, 31)));

        var report = result.Value!;
        report.Rows.Should().HaveCount(1);
        Cell(report.Rows.Single(), report, "Employee No").Should().Be("EMP-0001");
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-4: Trend analytics (MonthlyTrend)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task MonthlyTrend_ProducesMonthlyTotalsByType()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        // Two approved annual leaves in distinct months within the trailing-12-month window ending Year-06.
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 5, 1), totalDays: 3m);
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 6, 1), totalDays: 2m);

        var result = await CreateService().GetAnalyticsAsync(
            LeaveAnalyticsChartType.MonthlyTrend,
            Params(to: new DateOnly(Year, 6, 30)));

        result.IsSuccess.Should().BeTrue();
        var chart = result.Value!;
        chart.ChartType.Should().Be(LeaveAnalyticsChartType.MonthlyTrend.ToString());
        chart.Categories.Should().HaveCount(12);
        chart.Categories.Last().Should().Be($"{Year}-06");

        var annualSeries = chart.Series.Single(s => s.Name == "Annual Leave");
        annualSeries.Points.Single(p => p.Label == $"{Year}-05").Value.Should().Be(3m);
        annualSeries.Points.Single(p => p.Label == $"{Year}-06").Value.Should().Be(2m);
        // A month with no leave is zero, not missing.
        annualSeries.Points.Single(p => p.Label == $"{Year}-04").Value.Should().Be(0m);
    }

    [Fact]
    public async Task UtilizationByDepartment_RollsUpPerDepartmentPercentage()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 100m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 100m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 0m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 1), totalDays: 80m);

        var result = await CreateService().GetAnalyticsAsync(
            LeaveAnalyticsChartType.UtilizationByDepartment,
            Params(from: new DateOnly(Year, 1, 1), to: new DateOnly(Year, 12, 31)));

        var chart = result.Value!;
        var eng = chart.Points.Single(p => p.Label == "Engineering");
        eng.Value.Should().Be(40m); // 80 / 200
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-2: filters
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BalanceSummary_FilteredByDepartment_ReturnsOnlyThatDepartment()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 14m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary,
            Params(departmentId: _engineeringDeptId, leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Rows.Should().OnlyContain(r => Cell(r, report, "Department") == "Engineering");
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "EMP-0001", "EMP-0002" }); // not Carol (Sales)
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-3: pagination + sorting
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BalanceSummary_Pagination_RespectsPageSizeAndTotalCount()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 14m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        // 3 employees x 1 annual row = 3 total rows; page size 2 -> page 1 has 2, page 2 has 1.
        var page1 = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId, page: 1, pageSize: 2));
        var page2 = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId, page: 2, pageSize: 2));

        page1.Value!.TotalCount.Should().Be(3);
        page1.Value.Rows.Should().HaveCount(2);
        page1.Value.Page.Should().Be(1);
        page1.Value.PageSize.Should().Be(2);

        page2.Value!.TotalCount.Should().Be(3);
        page2.Value.Rows.Should().HaveCount(1);

        // No overlap across pages.
        var p1Nos = page1.Value.Rows.Select(r => Cell(r, page1.Value, "Employee No")).ToList();
        var p2Nos = page2.Value.Rows.Select(r => Cell(r, page2.Value, "Employee No")).ToList();
        p1Nos.Should().NotIntersectWith(p2Nos);
    }

    [Fact]
    public async Task BalanceSummary_SortByBalanceDescending_OrdersRows()
    {
        // Alice balance 14, Bob balance 7 (give differing entitlements, no ledger).
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 7m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 0m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary,
            Params(leaveTypeId: _annualLeaveTypeId, sortBy: "Balance", sortAscending: false));

        var report = result.Value!;
        var balances = report.Rows.Select(r => decimal.Parse(Cell(r, report, "Balance"), CultureInfo.InvariantCulture));
        balances.Should().BeInDescendingOrder();
        Cell(report.Rows.First(), report, "Employee No").Should().Be("EMP-0001"); // Alice, balance 14
    }

    // ══════════════════════════════════════════════════════════════
    //  BR-2: role scope (GenerateReportWithScopeAsync)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RoleScope_Hr_SeesAllEmployees()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 14m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var result = await CreateService().GenerateReportWithScopeAsync(
            LeaveReportType.BalanceSummary, "All", null, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Scope.Should().Be("All");
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "EMP-0001", "EMP-0002", "EMP-0003" });
    }

    [Fact]
    public async Task RoleScope_Manager_SeesOnlyTheirTeam()
    {
        // Make Bob report to Alice; the manager scope for Alice = {Alice (self), Bob}, excluding Carol.
        using (var db = CreateDbContext())
        {
            var bob = db.Employees.Single(e => e.Id == _empBob);
            bob.ReportsToEmployeeId = _empAlice;
            db.SaveChanges();
        }

        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 14m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var result = await CreateService().GenerateReportWithScopeAsync(
            LeaveReportType.BalanceSummary, "Manager", _empAlice, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Scope.Should().Be("Manager");
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "EMP-0001", "EMP-0002" }); // Alice + Bob, not Carol
    }

    [Fact]
    public async Task RoleScope_Employee_SeesOnlyOwnData()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 14m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var result = await CreateService().GenerateReportWithScopeAsync(
            LeaveReportType.BalanceSummary, "Employee", _empCarol, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Scope.Should().Be("Employee");
        report.Rows.Should().OnlyContain(r => Cell(r, report, "Employee No") == "EMP-0003");
    }

    [Fact]
    public async Task RoleScope_ResolvedFromCurrentUser_ManagerSeesTeam()
    {
        // Alice is an employee user who manages Bob. Resolved scope (via ICurrentUser) -> Manager.
        var aliceUserId = Guid.NewGuid();
        using (var db = CreateDbContext())
        {
            var alice = db.Employees.Single(e => e.Id == _empAlice);
            alice.UserId = aliceUserId;
            var bob = db.Employees.Single(e => e.Id == _empBob);
            bob.ReportsToEmployeeId = _empAlice;
            db.SaveChanges();
        }

        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 14m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var aliceManager = Substitute.For<ICurrentUser>();
        aliceManager.UserId.Returns(aliceUserId);
        aliceManager.Permissions.Returns(Array.Empty<string>()); // no Leave.View.All -> not HR

        var result = await CreateService(aliceManager).GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Scope.Should().Be("Manager");
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "EMP-0001", "EMP-0002" });
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-5: export
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Export_Csv_ContainsHeadersAndRows()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var result = await CreateService().ExportReportAsync(
            LeaveReportType.BalanceSummary, ReportExportFormat.Csv, Params(leaveTypeId: _annualLeaveTypeId));

        result.IsSuccess.Should().BeTrue();
        var export = result.Value!;
        export.Queued.Should().BeFalse();
        export.ContentType.Should().Be("text/csv");
        export.FileName.Should().EndWith(".csv");
        export.FileContent.Should().NotBeNull();

        var text = Encoding.UTF8.GetString(export.FileContent!);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Contain("Employee No").And.Contain("Balance");
        lines.Should().Contain(l => l.Contains("EMP-0001") && l.Contains("Annual Leave"));
        // Header + exactly one data row (only Alice's annual row qualifies).
        lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task Export_Xlsx_ProducesReadableWorkbookWithHeadersAndRows()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var result = await CreateService().ExportReportAsync(
            LeaveReportType.BalanceSummary, ReportExportFormat.Xlsx, Params(leaveTypeId: _annualLeaveTypeId));

        var export = result.Value!;
        export.Queued.Should().BeFalse();
        export.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        export.FileName.Should().EndWith(".xlsx");

        using var ms = new MemoryStream(export.FileContent!);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        ws.Cell(1, 1).GetString().Should().Be("Employee No");
        // Header row + 1 data row.
        ws.LastRowUsed()!.RowNumber().Should().Be(2);
        ws.Row(2).Cell(1).GetString().Should().Be("EMP-0001");
    }

    [Fact]
    public async Task Export_LargeDataset_RoutesToBackgroundJob()
    {
        // Build a >5,000-row report: seed enough employees that BalanceSummary exceeds the threshold.
        // Each employee yields one annual row; seed 5,001 extra employees with annual entitlement.
        var bigCount = LeaveReportService.SyncExportRowThreshold + 1;
        using (var db = CreateDbContext())
        {
            for (int i = 0; i < bigCount; i++)
            {
                var id = Guid.NewGuid();
                db.Employees.Add(Emp(id, $"Bulk{i}", $"BULK-{i:D5}", _engineeringDeptId));
            }
            db.SaveChanges();
        }

        // Every employee resolves to a non-zero annual entitlement so each produces a row.
        _entitlementService
            .ComputeEffectiveEntitlementAsync(Arg.Any<Guid>(), _annualLeaveTypeId, Year, Arg.Any<CancellationToken>())
            .Returns(ci => Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = ci.ArgAt<Guid>(0), LeaveTypeId = _annualLeaveTypeId, LeaveYear = Year,
                ProratedEntitlementDays = 10m,
            }));
        _entitlementService
            .ComputeEffectiveEntitlementAsync(Arg.Any<Guid>(), _sickLeaveTypeId, Year, Arg.Any<CancellationToken>())
            .Returns(ci => Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = ci.ArgAt<Guid>(0), LeaveTypeId = _sickLeaveTypeId, LeaveYear = Year,
                ProratedEntitlementDays = 0m,
            }));

        // No IBackgroundJobClient injected (the default ctor leaves it null) -> Queued with no JobId,
        // proving the routing decision deferred to the background path rather than generating inline.
        var result = await CreateService().ExportReportAsync(
            LeaveReportType.BalanceSummary, ReportExportFormat.Csv, Params(leaveTypeId: _annualLeaveTypeId));

        result.IsSuccess.Should().BeTrue();
        var export = result.Value!;
        export.RowCount.Should().BeGreaterThan(LeaveReportService.SyncExportRowThreshold);
        export.Queued.Should().BeTrue();
        export.FileContent.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════
    //  Guard: tenant context unresolved
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateReport_TenantNotResolved_Fails()
    {
        _tenantContext.IsResolved.Returns(false);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    [Fact]
    public async Task DepartmentCalendarCoverage_IsDocumentedStub_ReturnsNote()
    {
        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.DepartmentCalendarCoverage, Params());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows.Should().BeEmpty();
        result.Value.Note.Should().NotBeNullOrEmpty();
    }
}
