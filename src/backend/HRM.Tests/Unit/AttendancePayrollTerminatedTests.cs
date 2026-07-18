// ============================================================================
// US-ATT-009 BR-7 / ISSUE-091 — the payroll-data feed must include a TERMINATED
// employee's present/absent/LOP computed UP TO their last working day, not zeroed.
// Drives the real AttendancePayrollService + AttendanceSummaryService through the
// EF Core InMemory provider (mirrors ShiftServiceTests / RegularizationApprovalServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AttendancePayrollTerminatedTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    private Guid _employeeId;

    public AttendancePayrollTerminatedTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AttendancePayrollService Service()
    {
        var summary = new AttendanceSummaryService(
            Db(), _tenantContext, Substitute.For<IReportExportStorage>(),
            Substitute.For<ILogger<AttendanceSummaryService>>());
        return new AttendancePayrollService(
            Db(), _tenantContext, _currentUser, summary,
            Substitute.For<ILogger<AttendancePayrollService>>());
    }

    private static DateTime Utc(int y, int m, int d, int hour)
        => new DateTime(y, m, d, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PayrollData_TerminatedEmployee_ComputesPresentAbsentLop_UpToLastWorkingDay()
    {
        using (var db = Db())
        {
            // Terminated employee; no shift → every calendar day is a working day (deterministic).
            db.Employees.Add(new Employee
            {
                Id = _employeeId = Guid.NewGuid(), TenantId = _tenantId, UserId = Guid.NewGuid(),
                EmployeeNo = "EMP-TERM", FirstName = "Term", LastName = "Doe", Email = "term@t.com",
                Gender = Gender.Male, DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Terminated, IsActive = true,
            });

            // BR-7 cutoff: last working day = 2026-06-10 (status_change → Terminated).
            db.EmploymentHistories.Add(new EmploymentHistory
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId,
                ChangeType = "status_change", NewValue = "Terminated",
                EffectiveDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc), ChangedBy = "test",
            });

            // Present on Jun 1, 2, 3 (full days, 600 min ≥ standard). Jun 4..10 have no log → ABSENT → LOP.
            foreach (var day in new[] { 1, 2, 3 })
            {
                db.AttendanceLogs.Add(new AttendanceLog
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId,
                    ClockIn = Utc(2026, 6, day, 9), ClockOut = Utc(2026, 6, day, 19),
                    TotalWorkMinutes = 600, Source = "WEB",
                });
            }
            db.SaveChanges();
        }

        var result = await Service().GetPayrollDataAsync(2026, 6, new[] { _employeeId });

        result.IsSuccess.Should().BeTrue();
        var row = result.Value!.Rows.Should().ContainSingle().Subject;
        row.EmployeeId.Should().Be(_employeeId);
        // BR-7 window is Jun 1..Jun 10 (10 working days, no shift). 3 present, 7 absent → 7 LOP.
        row.TotalPresentDays.Should().Be(3m);
        row.TotalAbsentDays.Should().Be(7m);
        row.LopDays.Should().Be(7m);              // ISSUE-091: previously hard-zeroed for terminated staff.
        row.TotalWorkMinutes.Should().Be(1800);   // 3 × 600.
    }
}
