// ============================================================================
// ISSUE-223 regression: Terminated employees are "archived" and must be excluded
// from the default employee directory; an explicit includeTerminated flag brings
// them back (the archived/all view).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeArchiveFilterTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private Guid _deptId;
    private Guid _jtId;

    public EmployeeArchiveFilterTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private EmployeeService Service()
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
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(), cf,
            Substitute.For<IPayrollAuditLogger>(), NullLogger<EmployeeService>.Instance);
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

    private async Task<Guid> SeedEmployeeAsync(string email)
    {
        var result = await Service().CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "Test", LastName = "User", Email = email,
            DateOfJoining = DateTime.UtcNow.AddYears(-1),
            DepartmentId = _deptId, JobTitleId = _jtId, EmploymentType = EmploymentType.FullTime,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value!.Id;
    }

    private async Task SetTerminatedAsync(Guid employeeId)
    {
        using var db = Db();
        var e = await db.Employees.FirstAsync(x => x.Id == employeeId);
        e.Status = EmployeeStatus.Terminated;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAll_Default_ExcludesTerminated()
    {
        await SeedMasterDataAsync();
        var active = await SeedEmployeeAsync("active@acme.com");
        var terminated = await SeedEmployeeAsync("term@acme.com");
        await SetTerminatedAsync(terminated);

        var result = await Service().GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value!.Items.Select(i => i.Id).ToList();
        ids.Should().Contain(active);
        ids.Should().NotContain(terminated, "Terminated employees are archived out of the default list");
    }

    [Fact]
    public async Task GetAll_IncludeTerminated_ReturnsTerminated()
    {
        await SeedMasterDataAsync();
        var active = await SeedEmployeeAsync("active@acme.com");
        var terminated = await SeedEmployeeAsync("term@acme.com");
        await SetTerminatedAsync(terminated);

        var result = await Service().GetAllAsync(includeTerminated: true);

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value!.Items.Select(i => i.Id).ToList();
        ids.Should().Contain(active);
        ids.Should().Contain(terminated);
    }
}
