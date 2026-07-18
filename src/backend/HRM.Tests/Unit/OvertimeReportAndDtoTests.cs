// ============================================================================
// US-ATT-006: Overtime read-surface regression tests (OvertimeService).
// Covers ISSUE-079 (daily/weekly cap flags exposed on the overtime DTOs) and
// ISSUE-080 (UNAPPROVED minutes exposed on the monthly report). Uses the EF Core
// InMemory provider through the service (mirrors ShiftServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OvertimeReportAndDtoTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OvertimeService> _logger;

    private Guid _employeeId;

    public OvertimeReportAndDtoTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);

        _logger = Substitute.For<ILogger<OvertimeService>>();

        SeedEmployee();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);
    private OvertimeService Service() => new(Db(), _tenantContext, _currentUser, _logger);

    private void SeedEmployee()
    {
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = _employeeId = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId,
            EmployeeNo = "EMP-1", FirstName = "John", LastName = "Doe", Email = "j@t.com",
            Gender = Gender.Male, DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        db.SaveChanges();
    }

    private void SeedOvertime(
        DateOnly date, string status, int overtimeMinutes, int? approvedMinutes = null,
        bool dailyCapApplied = false, bool weeklyCapExceeded = false)
    {
        using var db = Db();
        db.OvertimeRecords.Add(new OvertimeRecord
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId,
            Date = date, OvertimeMinutes = overtimeMinutes, ApprovedMinutes = approvedMinutes,
            Multiplier = 1.5m, Type = OvertimeType.AutoDetected, Status = status,
            Reason = "Auto-detected overtime", DailyCapApplied = dailyCapApplied,
            WeeklyCapExceeded = weeklyCapExceeded,
        });
        db.SaveChanges();
    }

    // ── ISSUE-079: cap flags are exposed on the overtime DTO ────────────

    [Fact]
    public async Task GetMyOvertime_CarriesCapFlags_MatchingTheEntity()
    {
        var date = new DateOnly(2026, 6, 15);
        SeedOvertime(date, OvertimeStatus.Pending, overtimeMinutes: 240,
            dailyCapApplied: true, weeklyCapExceeded: true);

        var result = await Service().GetMyOvertimeAsync();

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Single();
        dto.OvertimeMinutes.Should().Be(240);
        dto.DailyCapApplied.Should().BeTrue();     // FR-8 / ISSUE-079: previously omitted from the DTO.
        dto.WeeklyCapExceeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetMyOvertime_UncappedRecord_ReportsFlagsFalse()
    {
        SeedOvertime(new DateOnly(2026, 6, 16), OvertimeStatus.Pending, overtimeMinutes: 90);

        var result = await Service().GetMyOvertimeAsync();

        var dto = result.Value!.Single();
        dto.DailyCapApplied.Should().BeFalse();
        dto.WeeklyCapExceeded.Should().BeFalse();
    }

    // ── ISSUE-080: monthly report exposes UNAPPROVED minutes ────────────

    [Fact]
    public async Task MonthlyReport_ExposesUnapprovedMinutes()
    {
        SeedOvertime(new DateOnly(2026, 6, 5), OvertimeStatus.Approved, overtimeMinutes: 120, approvedMinutes: 100);
        SeedOvertime(new DateOnly(2026, 6, 6), OvertimeStatus.Pending, overtimeMinutes: 60);
        SeedOvertime(new DateOnly(2026, 6, 7), OvertimeStatus.Rejected, overtimeMinutes: 45);
        SeedOvertime(new DateOnly(2026, 6, 8), OvertimeStatus.Unapproved, overtimeMinutes: 200);
        SeedOvertime(new DateOnly(2026, 6, 9), OvertimeStatus.Unapproved, overtimeMinutes: 30);

        var result = await Service().GetMonthlyReportAsync(2026, 6);

        result.IsSuccess.Should().BeTrue();
        var row = result.Value!.Items.Single();
        row.ApprovedMinutes.Should().Be(100);
        row.PendingMinutes.Should().Be(60);
        row.RejectedMinutes.Should().Be(45);
        row.UnapprovedMinutes.Should().Be(230);   // ISSUE-080: 200 + 30, previously invisible.
        row.RecordCount.Should().Be(5);

        result.Value.Totals.UnapprovedMinutes.Should().Be(230);
    }
}
