// ============================================================================
// BUG-119 regression: a self-service caller (Employee.Edit.Own, without the
// HR-level Employee.Edit) must be able to edit ONLY their own employee record.
// Editing another employee is horizontal privilege escalation → 403.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeSelfEditOwnershipTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public EmployeeSelfEditOwnershipTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private Guid _deptId;
    private Guid _jtId;

    private EmployeeService HrService()
    {
        var hr = Substitute.For<ICurrentUser>();
        hr.IsAuthenticated.Returns(true);
        hr.UserId.Returns(Guid.NewGuid());
        hr.Email.Returns("hr@acme.com");
        hr.Roles.Returns(new List<string> { "HR Officer" });
        hr.Permissions.Returns(new List<string> { "Employee.Edit", "Employee.View.All" });
        var cf = Substitute.For<ICustomFieldService>();
        cf.ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(HRM.Application.Common.Models.Result.Success());
        return new EmployeeService(Db(), _tenantContext, hr,
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(), cf, NullLogger<EmployeeService>.Instance);
    }

    private async Task SeedMasterDataAsync()
    {
        using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp", Status = TenantStatus.Active });
        _deptId = Guid.NewGuid();
        _jtId = Guid.NewGuid();
        db.Departments.Add(new Department { Id = _deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = _jtId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedEmployeeAsync(Guid userId, string email)
    {
        var result = await HrService().CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "Test",
            LastName = "User",
            Email = email,
            DateOfJoining = DateTime.UtcNow.AddYears(-1),
            DepartmentId = _deptId,
            JobTitleId = _jtId,
            EmploymentType = EmploymentType.FullTime,
            UserId = userId,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value!.Id;
    }

    private EmployeeService SelfServiceFor(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("employee@acme.com");
        currentUser.Roles.Returns(new List<string> { "Employee" });
        currentUser.Permissions.Returns(new List<string> { "Employee.Edit.Own", "Employee.View.Own" });

        return new EmployeeService(
            Db(), _tenantContext, currentUser,
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(),
            Substitute.For<ICustomFieldService>(), NullLogger<EmployeeService>.Instance);
    }

    [Fact]
    public async Task SelfServiceEdit_AnotherEmployeesRecord_IsForbidden()
    {
        await SeedMasterDataAsync();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedEmployeeAsync(userA, "a@acme.com");
        var empB = await SeedEmployeeAsync(userB, "b@acme.com");

        // User A (self-service) tries to edit employee B.
        var result = await SelfServiceFor(userA)
            .UpdateProfileAsync(empB, new UpdateEmployeeProfileRequest { RowVersion = 0 });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("your own profile");
    }

    [Fact]
    public async Task SelfServiceEdit_OwnRecord_PassesOwnershipCheck()
    {
        await SeedMasterDataAsync();
        var userA = Guid.NewGuid();
        var empA = await SeedEmployeeAsync(userA, "a@acme.com");

        var result = await SelfServiceFor(userA)
            .UpdateProfileAsync(empA, new UpdateEmployeeProfileRequest { RowVersion = 0 });

        // Ownership check passes; the empty request makes no changes.
        result.IsSuccess.Should().BeTrue();
    }
}
