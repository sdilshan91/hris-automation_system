// ============================================================================
// US-CHR-011: Organization Tree Reporting View Tests.
// Verifies that the reporting view now uses Employee.ReportsToEmployeeId
// instead of the previous best-effort department-manager fallback.
// ReportingViewAvailable should be true.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OrgTreeReportingViewTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OrganizationTreeService> _logger;

    public OrgTreeReportingViewTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
        _logger = Substitute.For<ILogger<OrganizationTreeService>>();
    }

    private OrganizationTreeService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new OrganizationTreeService(dbContext, _tenantContext, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private Employee CreateEmployee(string firstName, string lastName, Guid? reportsTo = null)
    {
        return new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeNo = $"EMP-{Random.Shared.Next(1000, 9999)}",
            FirstName = firstName,
            LastName = lastName,
            Email = $"{firstName.ToLower()}.{lastName.ToLower()}{Random.Shared.Next(1, 9999)}@test.com",
            DateOfJoining = DateTime.UtcNow.AddDays(-30),
            DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            IsDeleted = false,
            ReportsToEmployeeId = reportsTo,
        };
    }

    [Fact]
    public async Task GetReportingTree_ReturnsRealHierarchy_WithReportingViewAvailableTrue()
    {
        // Arrange: CEO -> VP -> Manager -> 2 Engineers
        var ceo = CreateEmployee("CEO", "Boss");
        var vp = CreateEmployee("VP", "Lead", reportsTo: ceo.Id);
        var mgr = CreateEmployee("Mgr", "Mid", reportsTo: vp.Id);
        var eng1 = CreateEmployee("Eng", "One", reportsTo: mgr.Id);
        var eng2 = CreateEmployee("Eng", "Two", reportsTo: mgr.Id);
        var noManager = CreateEmployee("Solo", "Contrib"); // root node (no manager)

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(ceo, vp, mgr, eng1, eng2, noManager);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.GetReportingTreeAsync(null, depth: 10, includeInactive: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ReportingViewAvailable.Should().BeTrue();
        result.Value.View.Should().Be("reporting");

        // Root nodes: CEO and Solo (no manager)
        result.Value.Nodes.Should().HaveCount(2);

        var ceoNode = result.Value.Nodes.First(n => n.NodeId == ceo.Id);
        ceoNode.Name.Should().Be("CEO Boss");
        ceoNode.ChildrenCount.Should().Be(1); // VP

        var vpNode = ceoNode.Children.First(n => n.NodeId == vp.Id);
        vpNode.ChildrenCount.Should().Be(1); // Mgr

        var mgrNode = vpNode.Children.First(n => n.NodeId == mgr.Id);
        mgrNode.ChildrenCount.Should().Be(2); // 2 engineers
    }

    [Fact]
    public async Task GetReportingTree_WithParentId_ReturnsDirectReportsOnly()
    {
        var ceo = CreateEmployee("CEO", "Boss");
        var emp1 = CreateEmployee("Emp", "One", reportsTo: ceo.Id);
        var emp2 = CreateEmployee("Emp", "Two", reportsTo: ceo.Id);
        var subEmp = CreateEmployee("Sub", "Emp", reportsTo: emp1.Id);

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(ceo, emp1, emp2, subEmp);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: Get direct reports of CEO
        var result = await service.GetReportingTreeAsync(ceo.Id, depth: 2, includeInactive: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ReportingViewAvailable.Should().BeTrue();
        result.Value.Nodes.Should().HaveCount(2); // emp1 and emp2
        result.Value.Nodes.Should().Contain(n => n.NodeId == emp1.Id);
        result.Value.Nodes.Should().Contain(n => n.NodeId == emp2.Id);
    }

    [Fact]
    public async Task GetReportingTree_EmployeesWithNoManager_AreRootNodes()
    {
        var emp1 = CreateEmployee("Root", "One");
        var emp2 = CreateEmployee("Root", "Two");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(emp1, emp2);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        var result = await service.GetReportingTreeAsync(null, depth: 2, includeInactive: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().HaveCount(2);
        result.Value.Nodes.Should().OnlyContain(n => n.ParentId == null);
    }

    public void Dispose()
    {
        // InMemory database is cleaned up automatically
    }
}
