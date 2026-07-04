// ============================================================================
// BUG-023 REGRESSION — Manager-assignment audit-write.
// Assigning a manager MUST write a queryable `audit_logs` (AuditLog) row
// (action Employee.ManagerAssigned) capturing the before/after managerId,
// mirroring the RoleService/LeaveTypeService audit pattern.
//
// Pre-fix (git show HEAD:...ReportingStructureService.cs) writes only an
// EmployeeFieldAuditLog row (a SEPARATE table) plus an ILogger line — nothing in
// audit_logs -> this test FAILS. Post-fix the audit_logs row is present -> PASSES.
//
// Harness mirrors the employee-seeding used by ReportingStructureServiceTests.
// The assertion queries ONLY db.AuditLogs (audit_logs), not EmployeeFieldAuditLogs.
// AuditLog has no query filter -> TenantId asserted directly.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ManagerAssignmentAuditWriteTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly ILogger<ReportingStructureService> _logger =
        Substitute.For<ILogger<ReportingStructureService>>();

    public ManagerAssignmentAuditWriteTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(_actorId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    private ReportingStructureService CreateService() =>
        new(TestDbContextFactory.Create(_tenantContext, _dbName), _tenantContext, _currentUser, _logger);

    private async Task<(Guid deptId, Guid jtId)> SeedRefs()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true,
        };
        var jt = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = "Software Engineer", IsActive = true,
        };
        db.Departments.Add(dept);
        db.JobTitles.Add(jt);
        await db.SaveChangesAsync();
        return (dept.Id, jt.Id);
    }

    private async Task<Guid> SeedEmployee(Guid deptId, Guid jtId, string email)
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var emp = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeNo = $"EMP-{Guid.NewGuid().ToString()[..4]}",
            FirstName = "First",
            LastName = "Last",
            Email = email,
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptId,
            JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    // ── BUG-023: assign manager → Employee.ManagerAssigned audit row ────────

    [Fact]
    public async Task AssignManager_WritesAuditRowWithBeforeAfter_BUG023()
    {
        var (deptId, jtId) = await SeedRefs();
        var employeeId = await SeedEmployee(deptId, jtId, "report@test.com");
        var managerId = await SeedEmployee(deptId, jtId, "manager@test.com");

        var result = await CreateService().AssignManagerAsync(employeeId, new AssignManagerRequest
        {
            ManagerEmployeeId = managerId,
            Reason = "New reporting line",
        });
        result.IsSuccess.Should().BeTrue();

        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var rows = await db.AuditLogs.Where(a => a.TenantId == _tenantId).ToListAsync();

        var audit = rows.SingleOrDefault(a =>
            (a.Action ?? a.EventType ?? string.Empty).Contains("ManagerAssigned"));
        audit.Should().NotBeNull("assigning a manager must write an Employee.ManagerAssigned audit_logs row");

        audit!.ResourceId.Should().Be(employeeId.ToString(), "the audit row must reference the employee whose manager changed");
        audit.TenantId.Should().Be(_tenantId, "the audit row must be tenant-scoped");
        audit.UserId.Should().Be(_actorId, "the audit row must be attributed to the acting user");

        // before had no manager, after has the assigned manager -> before ≠ after, after names the manager.
        audit.After.Should().NotBeNull();
        audit.After!.Should().Contain(managerId.ToString(), "the after-snapshot must record the new managerId");
        audit.Before.Should().NotBe(audit.After, "before/after must reflect the assignment change");
    }
}
