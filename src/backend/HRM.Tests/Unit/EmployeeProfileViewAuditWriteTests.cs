// ============================================================================
// BUG-010 REGRESSION — Employee-profile-view read-access audit-write.
// Viewing a profile (GET /employees/{id}/profile -> EmployeeService.GetProfileAsync)
// is a PII read-access event that MUST write a queryable `audit_logs` (AuditLog)
// row (action Employee.ProfileViewed), mirroring the RoleService/LeaveTypeService
// audit pattern.
//
// Pre-fix (git show HEAD:...EmployeeService.cs) GetProfileAsync is a pure
// AsNoTracking read that writes nothing -> this test FAILS. Post-fix the row is
// present -> it PASSES.
//
// The employee is seeded directly (no create op), so the ONLY audit_logs row after
// GetProfileAsync is the one the read op writes — proving the read is audited.
// Harness mirrors EmployeeProfileServiceTests. AuditLog has no query filter ->
// TenantId asserted directly.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeProfileViewAuditWriteTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IVirusScanner _virusScanner = Substitute.For<IVirusScanner>();
    private readonly ICustomFieldService _customFieldService = Substitute.For<ICustomFieldService>();
    private readonly ILogger<EmployeeService> _logger = Substitute.For<ILogger<EmployeeService>>();

    public EmployeeProfileViewAuditWriteTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(_actorId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new List<string> { "Employee.View.All" });
        _currentUser.Roles.Returns(new List<string> { "HR Officer" });

        _customFieldService.ValidateCustomFieldValuesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private EmployeeService CreateService() =>
        new(TestDbContextFactory.Create(_tenantContext, _dbName), _tenantContext, _currentUser,
            _fileStorage, _virusScanner, _customFieldService, _logger);

    private async Task<Guid> SeedEmployee()
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
        var emp = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeNo = $"EMP-{Guid.NewGuid().ToString()[..4]}",
            FirstName = "John", LastName = "Doe", Email = "john@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = dept.Id, JobTitleId = jt.Id,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        };
        db.Departments.Add(dept);
        db.JobTitles.Add(jt);
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    // ── BUG-010: view profile → Employee.ProfileViewed read-access audit ────

    [Fact]
    public async Task ViewProfile_WritesReadAccessAuditRow_BUG010()
    {
        var employeeId = await SeedEmployee();

        var result = await CreateService().GetProfileAsync(employeeId);
        result.IsSuccess.Should().BeTrue();

        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var rows = await db.AuditLogs.Where(a => a.TenantId == _tenantId).ToListAsync();

        var audit = rows.SingleOrDefault(a =>
            (a.Action ?? a.EventType ?? string.Empty).Contains("ProfileViewed"));
        audit.Should().NotBeNull("viewing a profile must write an Employee.ProfileViewed read-access audit row");

        audit!.ResourceId.Should().Be(employeeId.ToString(), "the audit row must reference the viewed employee");
        audit.TenantId.Should().Be(_tenantId, "the audit row must be tenant-scoped");
        audit.UserId.Should().Be(_actorId, "the read-access audit must be attributed to the viewing user");
    }
}
