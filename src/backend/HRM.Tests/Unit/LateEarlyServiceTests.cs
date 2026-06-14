// ============================================================================
// US-ATT-008: LateEarlyService unit tests — tenant late POLICY (FR-4), the team/HR
// REPORT (AC-5/FR-6/FR-7 chronic flag) and the employee self-SCORE (section 8).
// The report/score read the PERSISTED is_late/late_minutes/is_early_departure/
// early_departure_minutes columns on attendance_log (DRY — no recompute). Uses the EF
// Core InMemory provider through the service (mirrors RegularizationApprovalServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LateEarlyServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LateEarlyService> _logger;

    private Guid _managerId;
    private Guid _reportId;       // a direct report of the manager
    private Guid _strangerId;     // an employee NOT reporting to the manager

    public LateEarlyServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_managerUserId);
        _currentUser.Permissions.Returns(Array.Empty<string>());

        _logger = Substitute.For<ILogger<LateEarlyService>>();

        SeedOrg();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LateEarlyService Service()
        => new(Db(), _tenantContext, _currentUser, _logger);

    private void GrantViewAll()
        => _currentUser.Permissions.Returns(new[] { PermissionCatalog.Attendance.ViewAll });

    private void SeedOrg()
    {
        using var db = Db();
        _managerId = Guid.NewGuid();
        _reportId = Guid.NewGuid();
        _strangerId = Guid.NewGuid();

        db.Employees.AddRange(
            Emp(_managerId, _managerUserId, "Mary", "Manager", reportsTo: null),
            Emp(_reportId, Guid.NewGuid(), "Rita", "Report", reportsTo: _managerId),
            Emp(_strangerId, Guid.NewGuid(), "Sam", "Stranger", reportsTo: null));
        db.SaveChanges();
    }

    private Employee Emp(Guid id, Guid userId, string first, string last, Guid? reportsTo) => new()
    {
        Id = id,
        TenantId = _tenantId,
        UserId = userId,
        EmployeeNo = $"EMP-{id.ToString()[..4]}",
        FirstName = first,
        LastName = last,
        Email = $"{first}@test.com".ToLowerInvariant(),
        Gender = Gender.Female,
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active,
        IsActive = true,
        ReportsToEmployeeId = reportsTo,
    };

    /// <summary>Seeds an attendance_log carrying persisted late/early flags on a given date.</summary>
    private void SeedLog(
        Guid employeeId, DateOnly date,
        bool isLate = false, int lateMinutes = 0,
        bool isEarly = false, int earlyMinutes = 0)
    {
        using var db = Db();
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeId = employeeId,
            ClockIn = date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
            ClockOut = date.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Utc),
            Status = "COMPLETE",
            Source = "WEB",
            IsLate = isLate,
            LateMinutes = lateMinutes,
            IsEarlyDeparture = isEarly,
            EarlyDepartureMinutes = earlyMinutes,
        });
        db.SaveChanges();
    }

    private void SeedPolicy(int chronicThreshold = 5, int thresholdCount = 3, bool active = true)
    {
        using var db = Db();
        db.LatePolicies.Add(new LatePolicy
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            ThresholdCount = thresholdCount,
            DeductionDays = 0.5m,
            Period = LatePolicyPeriod.Monthly,
            NotificationOnLate = false,
            ChronicThreshold = chronicThreshold,
            IsActive = active,
        });
        db.SaveChanges();
    }

    private static DateOnly OnDay(int day) => new(2026, 5, day);

    // ── FR-4: policy get/upsert ─────────────────────────────────────────────

    [Fact]
    public async Task GetPolicy_NoneConfigured_ReturnsDefaults()
    {
        var result = await Service().GetPolicyAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.ThresholdCount.Should().Be(3);
        result.Value.Period.Should().Be("MONTHLY");
        result.Value.ChronicThreshold.Should().Be(5);
    }

    [Fact]
    public async Task UpsertPolicy_Valid_PersistsAndReturns()
    {
        var dto = new LatePolicyDto
        {
            ThresholdCount = 4,
            DeductionDays = 1.0m,
            Period = "monthly",          // case-insensitive, normalized to MONTHLY
            NotificationOnLate = true,
            ChronicThreshold = 6,
            IsActive = true,
        };

        var result = await Service().UpsertPolicyAsync(dto);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ThresholdCount.Should().Be(4);
        result.Value.Period.Should().Be("MONTHLY");
        result.Value.ChronicThreshold.Should().Be(6);

        // Re-read through a fresh service instance to confirm it persisted.
        var reread = await Service().GetPolicyAsync();
        reread.Value!.ThresholdCount.Should().Be(4);
        reread.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertPolicy_InvalidPeriod_Fails400()
    {
        var dto = new LatePolicyDto { Period = "WEEKLY", ThresholdCount = 3, ChronicThreshold = 5 };

        var result = await Service().UpsertPolicyAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_period");
    }

    [Fact]
    public async Task UpsertPolicy_ThresholdBelowOne_Fails400()
    {
        var dto = new LatePolicyDto { Period = "MONTHLY", ThresholdCount = 0, ChronicThreshold = 5 };

        var result = await Service().UpsertPolicyAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_threshold");
    }

    // ── AC-5 / FR-6: report ─────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_TeamScope_AggregatesDirectReportsLateAndEarly()
    {
        // Rita (direct report) has 2 lates + 1 early departure in May.
        SeedLog(_reportId, OnDay(4), isLate: true, lateMinutes: 20);
        SeedLog(_reportId, OnDay(5), isLate: true, lateMinutes: 5);
        SeedLog(_reportId, OnDay(6), isEarly: true, earlyMinutes: 30);
        // Sam (not a report) should never appear in a team-scoped report.
        SeedLog(_strangerId, OnDay(4), isLate: true, lateMinutes: 99);

        var result = await Service().GetReportAsync(OnDay(1), OnDay(31), null, null, "team");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows.Should().ContainSingle();
        var row = result.Value.Rows[0];
        row.EmployeeId.Should().Be(_reportId);
        row.LateCount.Should().Be(2);
        row.TotalLateMinutes.Should().Be(25);
        row.EarlyDepartureCount.Should().Be(1);
        row.TotalEarlyMinutes.Should().Be(30);
    }

    [Fact]
    public async Task GetReport_ChronicThresholdExceeded_FlagsChronic()
    {
        SeedPolicy(chronicThreshold: 2);
        SeedLog(_reportId, OnDay(4), isLate: true, lateMinutes: 10);
        SeedLog(_reportId, OnDay(5), isLate: true, lateMinutes: 10);
        SeedLog(_reportId, OnDay(6), isLate: true, lateMinutes: 10);   // 3 > 2 → chronic

        var result = await Service().GetReportAsync(OnDay(1), OnDay(31), null, null, "team");

        result.Value!.Rows.Single(r => r.EmployeeId == _reportId).IsChronic.Should().BeTrue();
    }

    [Fact]
    public async Task GetReport_AllScope_WithoutViewAllPermission_Fails403()
    {
        var result = await Service().GetReportAsync(OnDay(1), OnDay(31), null, null, "all");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("scope_not_allowed");
    }

    [Fact]
    public async Task GetReport_AllScope_WithViewAll_IncludesEveryEmployee()
    {
        GrantViewAll();
        SeedLog(_strangerId, OnDay(4), isLate: true, lateMinutes: 12);

        var result = await Service().GetReportAsync(OnDay(1), OnDay(31), null, null, "all");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows.Should().Contain(r => r.EmployeeId == _strangerId);
    }

    [Fact]
    public async Task GetReport_InvalidRange_Fails400()
    {
        var result = await Service().GetReportAsync(OnDay(10), OnDay(1), null, null, "team");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_range");
    }

    // ── section 8: self score ───────────────────────────────────────────────

    [Fact]
    public async Task GetMyScore_CountsActingEmployeeLatesAndAllowedFromPolicy()
    {
        SeedPolicy(chronicThreshold: 5);
        // The acting user is Mary the manager (UserId = _managerUserId → _managerId).
        SeedLog(_managerId, OnDay(4), isLate: true, lateMinutes: 15);
        SeedLog(_managerId, OnDay(5), isEarly: true, earlyMinutes: 20);

        var result = await Service().GetMyScoreAsync(2026, 5);

        result.IsSuccess.Should().BeTrue();
        result.Value!.YearMonth.Should().Be("2026-05");
        result.Value.LateCount.Should().Be(1);
        result.Value.EarlyDepartureCount.Should().Be(1);
        result.Value.AllowedLates.Should().Be(5);
    }

    [Fact]
    public async Task GetMyScore_InvalidMonth_Fails400()
    {
        var result = await Service().GetMyScoreAsync(2026, 13);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_month");
    }

    [Fact]
    public async Task GetReport_TenantNotResolved_Fails400()
    {
        _tenantContext.IsResolved.Returns(false);

        var result = await Service().GetReportAsync(OnDay(1), OnDay(31), null, null, "team");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
