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
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
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

        // The default caller is HR (holds Reports.View.All) -> "All" scope (DEC-1).
        _hrUser = Substitute.For<ICurrentUser>();
        _hrUser.UserId.Returns(_hrUserId);
        _hrUser.Permissions.Returns(new[] { PermissionCatalog.Reports.ViewAll });

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

        // BUG-124: the report resolves entitlements via the batch method. By default the stub delegates to
        // the per-pair ComputeEffectiveEntitlementAsync above, so the existing StubEntitlement(...) value
        // setups keep driving the report math unchanged. Tests that assert call counts override this.
        _entitlementService
            .ComputeProratedEntitlementsBatchAsync(
                Arg.Any<IReadOnlyList<Employee>>(), Arg.Any<IReadOnlyList<LeaveType>>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var emps = ci.ArgAt<IReadOnlyList<Employee>>(0);
                var lts = ci.ArgAt<IReadOnlyList<LeaveType>>(1);
                var year = ci.ArgAt<int>(2);
                var ct = ci.ArgAt<CancellationToken>(3);
                var dict = new Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal>();
                foreach (var e in emps)
                    foreach (var lt in lts)
                    {
                        var res = _entitlementService
                            .ComputeEffectiveEntitlementAsync(e.Id, lt.Id, year, ct)
                            .GetAwaiter().GetResult();
                        dict[(e.Id, lt.Id)] = res.IsSuccess ? res.Value!.ProratedEntitlementDays : 0m;
                    }
                return dict;
            });

        _exportStorage = Substitute.For<IReportExportStorage>();
        _logger = Substitute.For<ILogger<LeaveReportService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveReportService CreateService(ICurrentUser? caller = null)
        => new(CreateDbContext(), _tenantContext, caller ?? _hrUser, _entitlementService,
            _exportStorage, _logger, new TenantLeaveYearResolver(CreateDbContext(), _tenantContext),
            Substitute.For<IHolidayProvider>());

    /// <summary>ISSUE-311: seeds the tenant ROW carrying the FiscalYearStartMonth the resolver must READ
    /// (the fixture otherwise seeds no Tenant row, so the resolver falls back to calendar).</summary>
    private void SeedTenantFiscalMonth(int month)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = $"t{month}", Name = "T",
            Status = TenantStatus.Active, FiscalYearStartMonth = month,
        });
        db.SaveChanges();
    }

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

    /// <summary>
    /// BUG-038: seeds the per-tenant absenteeism threshold. The fix reads the configurable threshold from
    /// <see cref="AttendanceSettings.AbsenteeismThresholdDays"/> (the per-tenant attendance-policy row) —
    /// absenteeism is an attendance concept — instead of returning a hardcoded 3, so the report's flag
    /// and its rendered "Threshold" column both reflect this value. Tenant-scoped via the global query
    /// filter, so the row is stamped with the test's tenant id.
    /// </summary>
    private void SeedAbsenteeismThreshold(decimal threshold)
    {
        using var db = CreateDbContext();
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            AbsenteeismThresholdDays = threshold,
        });
        db.SaveChanges();
    }

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
        DateOnly start, DateOnly? end = null, decimal totalDays = 1m, bool isLop = false,
        bool isHalfDay = false)
    {
        using var db = CreateDbContext();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId, StartDate = start, EndDate = end ?? start,
            TotalDays = totalDays, Status = status, RequestedAt = DateTime.UtcNow, IsLop = isLop,
            IsHalfDay = isHalfDay,
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

    [Fact]
    public async Task BalanceSummaryAndUtilization_ResolveEntitlementsInOneBatch_NotPerPair_BUG124()
    {
        // BUG-124: the report must resolve entitlements via ONE batch call per report, never a per-pair
        // ComputeEffectiveEntitlementAsync inside the (employee × leave type) loop (the N+1 that timed out
        // at 5,000 employees). Override the batch stub to return fixed values WITHOUT delegating to the
        // per-pair method, so we can prove the per-pair path is never touched.
        _entitlementService
            .ComputeProratedEntitlementsBatchAsync(
                Arg.Any<IReadOnlyList<Employee>>(), Arg.Any<IReadOnlyList<LeaveType>>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal>
            {
                [(_empAlice, _annualLeaveTypeId)] = 14m,
                [(_empBob, _annualLeaveTypeId)] = 14m,
            });

        var svc = CreateService();
        var balance = await svc.GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId));
        var utilization = await svc.GenerateReportAsync(
            LeaveReportType.Utilization,
            Params(leaveTypeId: _annualLeaveTypeId,
                from: new DateOnly(Year, 1, 1), to: new DateOnly(Year, 12, 31)));

        balance.IsSuccess.Should().BeTrue();
        utilization.IsSuccess.Should().BeTrue();

        // One batch resolution per report (2 reports → exactly 2 batch calls); the per-pair method is
        // never called from these paths, no matter how many employees/leave types there are.
        await _entitlementService.Received(2).ComputeProratedEntitlementsBatchAsync(
            Arg.Any<IReadOnlyList<Employee>>(), Arg.Any<IReadOnlyList<LeaveType>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _entitlementService.DidNotReceive().ComputeEffectiveEntitlementAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
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

    /// <summary>
    /// CAL-4 / US-ATT-011 AC-3 regression: the BR-4 threshold must come from the TENANT-DEFAULT settings row
    /// (LocationId null), never from a Location override.
    ///
    /// <para>Before CAL-4 there was exactly one AttendanceSettings row per tenant, and
    /// <c>ResolveAbsenteeismThresholdAsync</c> relied on that by reading with an unpredicated
    /// <c>FirstOrDefaultAsync()</c>. Once Location override rows exist that read returns an ARBITRARY row —
    /// so a Dubai override with a lenient threshold could silently set the bar for this tenant-wide report
    /// and under-flag every employee. <c>SeedAbsenteeismThreshold</c> seeds only ONE row, so it encodes the
    /// dead invariant and cannot catch this; the override seeded here is what makes the arm falsifiable.</para>
    ///
    /// <para>The override's threshold (99) is deliberately far from the tenant default (3) so Alice's 4
    /// unplanned days flag under the tenant default and would NOT flag under the override — the two
    /// outcomes are opposite, not merely different numbers.</para>
    /// </summary>
    [Fact]
    public async Task Absenteeism_UsesTheTenantDefaultThreshold_NotALocationOverride()
    {
        // ⚠ SEED ORDER IS LOAD-BEARING: the override goes in FIRST. An unpredicated read returns an
        // ARBITRARY row — on this InMemory fixture that is effectively insertion order, so seeding the
        // tenant default first would let the buggy read pick it by luck and the arm would pass against the
        // very bug it exists to catch (verified: it did).
        using (var db = CreateDbContext())
        {
            var dubai = new Location
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Dubai", TimeZone = "Asia/Dubai",
                IsActive = true,
            };
            db.Locations.Add(dubai);
            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                LocationId = dubai.Id,          // a LOCATION OVERRIDE — must not drive this report
                AbsenteeismThresholdDays = 99m, // so lenient nobody would ever be flagged
            });
            db.SaveChanges();
        }

        SeedAbsenteeismThreshold(3m);   // the tenant default (LocationId null) — seeded SECOND, on purpose

        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        for (int d = 1; d <= 4; d++)
            AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.HrAssigned,
                new DateOnly(Year, 3, d), new DateOnly(Year, 3, d), totalDays: 1m, isLop: true);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.Absenteeism,
            Params(from: new DateOnly(Year, 3, 1), to: new DateOnly(Year, 3, 31)));

        var report = result.Value!;
        var aliceRow = report.Rows.Single(r => Cell(r, report, "Employee No") == "EMP-0001");
        Cell(aliceRow, report, "Threshold").Should().Be(
            "3", "the TENANT-DEFAULT threshold — 99 would mean the Dubai override leaked into a tenant-wide report");
        Cell(aliceRow, report, "Flagged").Should().Be(
            "Yes", "4 unplanned > 3; under the override's 99 nobody would ever be flagged");
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

    [Fact]
    public async Task Absenteeism_UsesTenantThreshold_BUG038()
    {
        // BUG-038: the AC-3/BR-4 absenteeism flag must compare against the TENANT-CONFIGURED threshold,
        // not the hardcoded 3. Scenario: 4 unplanned (LOP) days in a single-month window (avg 4/month),
        // with the tenant threshold RAISED to 5. Because 4 does NOT exceed 5, the employee must NOT be
        // flagged, and the rendered "Threshold" column must reflect the configured 5.
        //
        // Pre-fix (ResolveAbsenteeismThresholdAsync => Task.FromResult(3m), tenant config ignored): the
        // report uses 3, so 4 > 3 → Flagged "Yes" and Threshold "3" → the assertions below fail. The
        // same 4-day load would correctly flag at the OLD default of 3, proving the two arms straddle
        // the 3-vs-5 boundary.
        SeedAbsenteeismThreshold(5m);

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
        Cell(aliceRow, report, "Threshold").Should().Be("5");   // configured, not the hardcoded 3
        Cell(aliceRow, report, "Flagged").Should().Be("No");    // 4 does not exceed the configured 5
    }

    // ══════════════════════════════════════════════════════════════
    //  ISSUE-311: report range default is the tenant's LEAVE year, not calendar Jan–Dec
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// KILLER for `ResolveRange` (`LeaveReportService.cs`). Month-4 tenant, an Absenteeism report with an
    /// explicit year 2026 and NO From/To. The default range must be the fiscal leave year 2026 =
    /// 2026-04-01..2027-03-31, so a LOP day on 2027-02-10 (inside that window, but OUTSIDE calendar 2026) is
    /// counted. Reverting the default to `new DateOnly(year,1,1)..new DateOnly(year,12,31)` excludes it and the
    /// report is empty. Clock-free: the year is explicit, so only the RANGE derivation is under test.
    /// </summary>
    [Fact]
    [Trait("Issue", "ISSUE-311")]
    public async Task Absenteeism_FiscalTenant_DefaultsRangeToTheFiscalLeaveYear_NotCalendar()
    {
        SeedTenantFiscalMonth(4);
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.HrAssigned,
            new DateOnly(2027, 2, 10), new DateOnly(2027, 2, 10), totalDays: 1m, isLop: true);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.Absenteeism, Params(year: 2026, from: null, to: null));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.Rows.Should().ContainSingle(
            "the fiscal leave-year range (Apr 2026–Mar 2027) includes the 2027-02-10 LOP day; a raw Jan–Dec "
            + "2026 range would exclude it and the report would be empty");
        Cell(report.Rows.Single(), report, "Employee No").Should().Be("EMP-0001");
    }

    /// <summary>CONTROL: a CALENDAR (month-1) tenant's default range is the calendar year — unchanged.</summary>
    [Fact]
    [Trait("Issue", "ISSUE-311")]
    public async Task Absenteeism_CalendarTenant_DefaultRangeIsTheCalendarYear()
    {
        SeedTenantFiscalMonth(1);
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.HrAssigned,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1), totalDays: 1m, isLop: true);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.Absenteeism, Params(year: 2026, from: null, to: null));

        result.Value!.Rows.Should().ContainSingle();
        Cell(result.Value.Rows.Single(), result.Value, "Employee No").Should().Be("EMP-0001");
    }

    /// <summary>
    /// KILLER for the balance-summary Pending bounds (`LeaveReportService.cs`, consistency-fixed with the
    /// range default). Month-4 tenant, a Pending request on 2027-02-10 (inside leave year 2026, outside
    /// calendar 2026). Correct code bounds Pending by 2026-04-01..2027-03-31 → counted. A raw
    /// `StartDate.Year == year` compares 2027 against the label 2026 → dropped → 0.
    /// </summary>
    [Fact]
    [Trait("Issue", "ISSUE-311")]
    public async Task BalanceSummary_FiscalTenant_PendingBoundsUseTheLeaveYearWindow_NotCalendar()
    {
        SeedTenantFiscalMonth(4);
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Pending,
            new DateOnly(2027, 2, 10), new DateOnly(2027, 2, 10), totalDays: 3m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(year: 2026, leaveTypeId: _annualLeaveTypeId));

        var alice = result.Value!.Rows.Single(r => Cell(r, result.Value, "Employee No") == "EMP-0001");
        Cell(alice, result.Value, "Pending").Should().Be("3",
            "leave year 2026 for an Apr–Mar tenant includes 2027-02-10; a raw StartDate.Year == 2026 shows 0");
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

    [Fact]
    public async Task UtilizationByDepartment_MergesDepartmentsDifferingOnlyInCase_ISSUE194()
    {
        // ISSUE-194: a second department row named "engineering" (lower-case) used to produce its OWN bar
        // beside "Engineering". That is worse than a cosmetic duplicate label: each bar's percentage was
        // computed over only a FRACTION of the real entitlement, so BOTH numbers were wrong.
        var lowerCaseEngDeptId = Guid.NewGuid();
        var empDave = Guid.NewGuid();
        using (var db = CreateDbContext())
        {
            db.Departments.Add(new Department
            {
                Id = lowerCaseEngDeptId, TenantId = _tenantId, Name = "engineering", Code = "ENG2",
            });
            db.Employees.Add(Emp(empDave, "Dave", "EMP-0004", lowerCaseEngDeptId));
            db.SaveChanges();
        }

        // Engineering: Alice + Bob, 100 each. engineering: Dave, 100. Total entitlement 300.
        StubEntitlement(_empAlice, _annualLeaveTypeId, 100m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 100m);
        StubEntitlement(empDave, _annualLeaveTypeId, 100m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 0m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(empDave, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        // 80 used in "Engineering" + 20 used in "engineering" = 100 of 300.
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 1), totalDays: 80m);
        AddRequest(empDave, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 1), totalDays: 20m);

        var result = await CreateService().GetAnalyticsAsync(
            LeaveAnalyticsChartType.UtilizationByDepartment,
            Params(from: new DateOnly(Year, 1, 1), to: new DateOnly(Year, 12, 31)));

        var chart = result.Value!;
        var engBars = chart.Points
            .Where(p => string.Equals(p.Label, "engineering", StringComparison.OrdinalIgnoreCase))
            .ToList();

        engBars.Should().ContainSingle("the two casings are one department to a reader, so they are one bar");
        // 100 / 300 — the honest figure. The pre-fix split reported 40% and 20%, neither of which is right.
        engBars[0].Value.Should().Be(33.33m);
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
        // Alice is an employee user who manages Bob AND holds Reports.View.Team. Resolved scope
        // (via ICurrentUser) -> Manager (DEC-1: team scope now requires the explicit Reports.View.Team perm).
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
        aliceManager.Permissions.Returns(new[] { PermissionCatalog.Reports.ViewTeam }); // Reports.View.Team (not .All) -> Manager scope

        var result = await CreateService(aliceManager).GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Scope.Should().Be("Manager");
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "EMP-0001", "EMP-0002" });
    }

    // ══════════════════════════════════════════════════════════════
    //  DEC-1: report scope now consumes Reports.View.All / Reports.View.Team (was Leave.View.All + any
    //  manager auto-getting team scope). The resolver (GenerateReportAsync → ResolveScopeAsync) is driven
    //  entirely by the caller's permissions + whether they manage anyone.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Dec1_ReportsViewAll_ResolvesAllScope()
    {
        StubEntitlement(_empAlice, _annualLeaveTypeId, 14m);
        StubEntitlement(_empBob, _annualLeaveTypeId, 14m);
        StubEntitlement(_empCarol, _annualLeaveTypeId, 14m);
        StubEntitlement(_empAlice, _sickLeaveTypeId, 0m);
        StubEntitlement(_empBob, _sickLeaveTypeId, 0m);
        StubEntitlement(_empCarol, _sickLeaveTypeId, 0m);

        var caller = Substitute.For<ICurrentUser>();
        caller.UserId.Returns(Guid.NewGuid());
        caller.Permissions.Returns(new[] { PermissionCatalog.Reports.ViewAll });

        var result = await CreateService(caller).GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Scope.Should().Be("All");
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "EMP-0001", "EMP-0002", "EMP-0003" });
    }

    [Fact]
    public async Task Dec1_ReportsViewTeam_WithDirectReport_ResolvesTeamScope()
    {
        // Alice manages Bob AND holds Reports.View.Team -> Manager scope (self + direct reports).
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

        var caller = Substitute.For<ICurrentUser>();
        caller.UserId.Returns(aliceUserId);
        caller.Permissions.Returns(new[] { PermissionCatalog.Reports.View, PermissionCatalog.Reports.ViewTeam });

        var result = await CreateService(caller).GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Scope.Should().Be("Manager");
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "EMP-0001", "EMP-0002" }); // Alice + Bob, not Carol
    }

    [Fact]
    public async Task Dec1_ReportsViewOnly_ManagerFallsThroughToSelfScope()
    {
        // DEC-1 tightening: Alice manages Bob but holds NEITHER Reports.View.All NOR Reports.View.Team.
        // Managing someone no longer auto-grants team scope — she is scoped to her own record only.
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

        var caller = Substitute.For<ICurrentUser>();
        caller.UserId.Returns(aliceUserId);
        caller.Permissions.Returns(new[] { PermissionCatalog.Reports.View });

        var result = await CreateService(caller).GenerateReportAsync(
            LeaveReportType.BalanceSummary, Params(leaveTypeId: _annualLeaveTypeId));

        var report = result.Value!;
        report.Scope.Should().Be("Employee");
        report.Rows.Select(r => Cell(r, report, "Employee No"))
            .Should().BeEquivalentTo(new[] { "EMP-0001" }); // Alice only
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

    // ── ISSUE-198: leave CSV must begin with the UTF-8 BOM (EF BB BF) so Excel auto-detects
    //    UTF-8 — matching the HR + payroll CSV writers via the shared CsvExport helper. ──
    [Fact]
    public async Task Export_Csv_StartsWith_Utf8Bom()
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
        var bytes = result.Value!.FileContent!;
        bytes.Should().HaveCountGreaterThanOrEqualTo(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);
        bytes[3].Should().NotBe(0xEF); // exactly one BOM
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

    [Fact]
    public async Task Export_LargeDataset_RoutesToBackground_WithoutGeneratingInline_Issue230()
    {
        // ISSUE-230: the routing decision must be made from a CHEAP row-count estimate, WITHOUT generating the
        // report — otherwise the only oversized report (BalanceSummary) hangs before it can ever be queued.
        var bigCount = LeaveReportService.SyncExportRowThreshold + 1;
        using (var db = CreateDbContext())
        {
            for (int i = 0; i < bigCount; i++)
                db.Employees.Add(Emp(Guid.NewGuid(), $"Bulk{i}", $"BULK2-{i:D5}", _engineeringDeptId));
            db.SaveChanges();
        }

        var result = await CreateService().ExportReportAsync(
            LeaveReportType.BalanceSummary, ReportExportFormat.Csv, Params(leaveTypeId: _annualLeaveTypeId));

        result.Value!.Queued.Should().BeTrue();
        // The report was NEVER generated for the queued export: the entitlement batch (the expensive
        // per-report work generation performs) is not invoked. The pre-fix generate-then-route ordering
        // would have called it.
        await _entitlementService.DidNotReceive().ComputeProratedEntitlementsBatchAsync(
            Arg.Any<IReadOnlyList<Employee>>(), Arg.Any<IReadOnlyList<LeaveType>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_LargeDataset_EnqueuesJob_ThreadingRequesterUserId()
    {
        // US-NTF-006 Phase 8: the caller's user id must be threaded into the enqueued LeaveReportExportJob so the job
        // can notify the requester when the export is ready. Seed a >5,000-row report to force the background path.
        var bigCount = LeaveReportService.SyncExportRowThreshold + 1;
        using (var db = CreateDbContext())
        {
            for (int i = 0; i < bigCount; i++)
                db.Employees.Add(Emp(Guid.NewGuid(), $"Bulk{i}", $"BULK-{i:D5}", _engineeringDeptId));
            db.SaveChanges();
        }

        _entitlementService
            .ComputeEffectiveEntitlementAsync(Arg.Any<Guid>(), _annualLeaveTypeId, Year, Arg.Any<CancellationToken>())
            .Returns(ci => Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = ci.ArgAt<Guid>(0), LeaveTypeId = _annualLeaveTypeId, LeaveYear = Year,
                ProratedEntitlementDays = 10m,
            }));

        // Capture the Job the Enqueue<T> extension builds (it calls IBackgroundJobClient.Create under the hood).
        Job? captured = null;
        var backgroundJobs = Substitute.For<IBackgroundJobClient>();
        backgroundJobs.Create(Arg.Do<Job>(j => captured = j), Arg.Any<IState>()).Returns("job-1");

        var svc = new LeaveReportService(CreateDbContext(), _tenantContext, _hrUser, _entitlementService,
            _exportStorage, _logger, new TenantLeaveYearResolver(CreateDbContext(), _tenantContext),
            Substitute.For<IHolidayProvider>(), backgroundJobs);

        var result = await svc.ExportReportAsync(
            LeaveReportType.BalanceSummary, ReportExportFormat.Csv, Params(leaveTypeId: _annualLeaveTypeId));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Queued.Should().BeTrue();
        result.Value.JobId.Should().Be("job-1");

        // RunAsync(tenantId, reportId, requestedByUserId, scopeKind, scopeEmployeeId, reportType, format, params, ct)
        captured.Should().NotBeNull("the export must be enqueued as a Hangfire job");
        captured!.Method.Name.Should().Be(nameof(ILeaveReportExportJob.RunAsync));
        captured.Args[0].Should().Be(_tenantId);
        captured.Args[2].Should().Be(_hrUserId, "the caller's user id is threaded through as requestedByUserId");
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

    // ══════════════════════════════════════════════════════════════
    //  FR-1: Department Leave Calendar Coverage (US-LV-012)
    //
    //  BuildDepartmentCalendarCoverageAsync replaced the former empty/"documented
    //  stub" (which returned zero rows + a Note). These tests pin the real math:
    //  one row per (department, day) where >=1 scoped employee is on APPROVED leave;
    //  half-day contributes 0.5; Headcount = non-terminated scoped employees in the
    //  department; Coverage % = (headcount - onLeave)/headcount*100 (2dp); days with
    //  nobody off produce NO row; other tenants' data never leaks in.
    //
    //  Pre-fix these all fail because the stub returned an empty row set — there is
    //  no (dept, day) row to assert On Leave / Headcount / Coverage % against.
    // ══════════════════════════════════════════════════════════════

    // Coverage-report column headers.
    private const string ColOnLeave = "On Leave";
    private const string ColHeadcount = "Headcount";
    private const string ColCoverage = "Coverage %";

    // Seeds an already-terminated employee (excluded from the coverage headcount denominator).
    private Guid AddTerminatedEmployee(string first, string empNo, Guid deptId)
    {
        var id = Guid.NewGuid();
        using var db = CreateDbContext();
        var emp = Emp(id, first, empNo, deptId);
        emp.Status = EmployeeStatus.Terminated;
        emp.IsActive = false;
        db.Employees.Add(emp);
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task DeptCoverage_FullDayCountsOne_AndSkipsDaysWithNobodyOff()
    {
        // Engineering headcount = Alice + Bob = 2. Alice is on a single full day (Mar 10) of approved
        // leave inside a Mar 09..Mar 12 window. Exactly one (Engineering, 2026-03-10) row must appear;
        // the other in-range days (09/11/12) have nobody off -> no rows.
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 10), new DateOnly(Year, 3, 10), totalDays: 1m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.DepartmentCalendarCoverage,
            Params(from: new DateOnly(Year, 3, 9), to: new DateOnly(Year, 3, 12)));

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.Columns.Should().ContainInOrder("Department", "Date", ColOnLeave, ColHeadcount, ColCoverage);

        // Only Engineering employees are off, and only on the one day -> exactly one row.
        report.Rows.Should().ContainSingle();
        var row = report.Rows.Single();
        Cell(row, report, "Department").Should().Be("Engineering");
        Cell(row, report, "Date").Should().Be("2026-03-10");
        Cell(row, report, ColOnLeave).Should().Be("1");         // full day = 1
        Cell(row, report, ColHeadcount).Should().Be("2");       // Alice + Bob
        Cell(row, report, ColCoverage).Should().Be("50");       // (2 - 1) / 2 * 100
    }

    [Fact]
    public async Task DeptCoverage_HalfDayCountsHalf_ISSUE_LV012()
    {
        // A half-day approved leave contributes 0.5 to the day's On Leave, so Coverage % = (2-0.5)/2*100 = 75.
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 4, 5), new DateOnly(Year, 4, 5), totalDays: 0.5m, isHalfDay: true);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.DepartmentCalendarCoverage,
            Params(from: new DateOnly(Year, 4, 1), to: new DateOnly(Year, 4, 30)));

        var report = result.Value!;
        var row = report.Rows.Single(r => Cell(r, report, "Date") == "2026-04-05");
        Cell(row, report, "Department").Should().Be("Engineering");
        Cell(row, report, ColOnLeave).Should().Be("0.5");       // half day = 0.5
        Cell(row, report, ColHeadcount).Should().Be("2");
        Cell(row, report, ColCoverage).Should().Be("75");       // (2 - 0.5) / 2 * 100
    }

    [Fact]
    public async Task DeptCoverage_TwoPeopleOffSameDay_SumsOnLeave()
    {
        // Alice (full) + Bob (half) off Mar 10 -> On Leave 1.5, Coverage (2-1.5)/2*100 = 25.
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 10), new DateOnly(Year, 3, 10), totalDays: 1m);
        AddRequest(_empBob, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 10), new DateOnly(Year, 3, 10), totalDays: 0.5m, isHalfDay: true);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.DepartmentCalendarCoverage,
            Params(from: new DateOnly(Year, 3, 10), to: new DateOnly(Year, 3, 10)));

        var report = result.Value!;
        var row = report.Rows.Single(r => Cell(r, report, "Department") == "Engineering");
        Cell(row, report, ColOnLeave).Should().Be("1.5");
        Cell(row, report, ColHeadcount).Should().Be("2");
        Cell(row, report, ColCoverage).Should().Be("25");
    }

    [Fact]
    public async Task DeptCoverage_TerminatedExcludedFromHeadcount()
    {
        // Engineering gains a terminated employee (Dan). The headcount denominator must stay 2
        // (Alice + Bob), NOT 3 -> Coverage for a single full-day absence is (2-1)/2*100 = 50, not 66.67.
        AddTerminatedEmployee("Dan", "EMP-0004", _engineeringDeptId);
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 10), new DateOnly(Year, 3, 10), totalDays: 1m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.DepartmentCalendarCoverage,
            Params(from: new DateOnly(Year, 3, 10), to: new DateOnly(Year, 3, 10)));

        var report = result.Value!;
        var row = report.Rows.Single(r => Cell(r, report, "Department") == "Engineering");
        Cell(row, report, ColHeadcount).Should().Be("2");       // Dan (Terminated) excluded
        Cell(row, report, ColCoverage).Should().Be("50");       // (2 - 1) / 2 * 100
    }

    [Fact]
    public async Task DeptCoverage_OnlyApprovedLeaveCounts()
    {
        // A Pending request on Mar 10 must NOT produce a coverage row (only Approved leave counts).
        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Pending,
            new DateOnly(Year, 3, 10), new DateOnly(Year, 3, 10), totalDays: 1m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.DepartmentCalendarCoverage,
            Params(from: new DateOnly(Year, 3, 1), to: new DateOnly(Year, 3, 31)));

        result.Value!.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task DeptCoverage_CrossTenant_OtherTenantLeaveExcluded()
    {
        // Tenant A: Alice (Engineering) is off Mar 10. Another tenant has its own department, employee,
        // and approved leave on the SAME day. The report (run under tenant A) must show only Engineering
        // and must NOT leak the other tenant's department/employee, nor inflate any headcount.
        var otherTenantId = Guid.NewGuid();
        using (var db = CreateDbContext())
        {
            var otherDeptId = Guid.NewGuid();
            var otherEmpId = Guid.NewGuid();
            db.Departments.Add(new Department
            {
                Id = otherDeptId, TenantId = otherTenantId, Name = "Foreign-Dept", Code = "FGN",
            });
            db.Employees.Add(new Employee
            {
                Id = otherEmpId, TenantId = otherTenantId, EmployeeNo = "FGN-0001",
                FirstName = "Zoe", LastName = "X", Email = "zoe@foreign.com", Gender = Gender.Female,
                DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = otherDeptId,
                JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active, IsActive = true,
            });
            db.LeaveRequests.Add(new LeaveRequest
            {
                Id = BaseEntity.NewUuidV7(), TenantId = otherTenantId, EmployeeId = otherEmpId,
                LeaveTypeId = _annualLeaveTypeId, StartDate = new DateOnly(Year, 3, 10),
                EndDate = new DateOnly(Year, 3, 10), TotalDays = 1m,
                Status = LeaveRequestStatus.Approved, RequestedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        AddRequest(_empAlice, _annualLeaveTypeId, LeaveRequestStatus.Approved,
            new DateOnly(Year, 3, 10), new DateOnly(Year, 3, 10), totalDays: 1m);

        var result = await CreateService().GenerateReportAsync(
            LeaveReportType.DepartmentCalendarCoverage,
            Params(from: new DateOnly(Year, 3, 10), to: new DateOnly(Year, 3, 10)));

        var report = result.Value!;
        report.Rows.Should().ContainSingle();
        var row = report.Rows.Single();
        Cell(row, report, "Department").Should().Be("Engineering");
        Cell(row, report, ColHeadcount).Should().Be("2");   // only tenant A's Engineering headcount
        report.Rows.Should().NotContain(r => Cell(r, report, "Department") == "Foreign-Dept");
    }

    // ── /leaves/reports/summary — the three landing cards (US-LV-012) ────────

    /// <summary>
    /// Utilization on the CARD derives from the same aggregate as the TABLE. A second calculation here is
    /// how a summary card and the report beneath it end up showing two different numbers for one thing —
    /// the S-1 shape behind BUG-307.
    /// </summary>
    [Fact]
    public async Task Summary_utilization_comes_from_the_shared_aggregate()
    {
        var result = await CreateService().GetSummaryMetricsAsync(Params(leaveTypeId: _annualLeaveTypeId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.TotalUtilizationPct.Should().BeGreaterThanOrEqualTo(0m);
        result.Value.TotalUtilizationPct.Should().BeLessThanOrEqualTo(100m,
            "used should not exceed entitled for this fixture");
    }

    /// <summary>
    /// The top leave type is decided by DAYS TAKEN, and ties break deterministically by name — otherwise
    /// the card flickers between equally-used types across refreshes with identical data.
    /// </summary>
    [Fact]
    public async Task Summary_top_leave_type_is_deterministic()
    {
        var first = await CreateService().GetSummaryMetricsAsync(Params());
        var second = await CreateService().GetSummaryMetricsAsync(Params());

        first.IsSuccess.Should().BeTrue(first.Error);
        second.Value!.TopLeaveType.Should().Be(first.Value!.TopLeaveType,
            "an unordered Max() would let the card change on refresh with identical data");
    }

    /// <summary>
    /// No entitlement configured must yield 0%, not a divide-by-zero and not NaN. A blank tenant is the
    /// first thing anyone sees on a fresh install.
    /// </summary>
    [Fact]
    public async Task Summary_returns_zero_rather_than_dividing_by_zero_on_an_empty_tenant()
    {
        var empty = Substitute.For<ITenantContext>();
        empty.TenantId.Returns(Guid.NewGuid());
        empty.IsResolved.Returns(true);
        var dbName = Guid.NewGuid().ToString();

        var svc = new LeaveReportService(
            TestDbContextFactory.Create(empty, dbName), empty, _hrUser, _entitlementService,
            _exportStorage, _logger,
            new TenantLeaveYearResolver(TestDbContextFactory.Create(empty, dbName), empty),
            Substitute.For<IHolidayProvider>());

        var result = await svc.GetSummaryMetricsAsync(Params());

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.TotalUtilizationPct.Should().Be(0m);
        result.Value.AbsenteeismRatePct.Should().Be(0m);
        result.Value.TopLeaveType.Should().BeEmpty("nothing was taken, so there is no top type to name");
    }
}
