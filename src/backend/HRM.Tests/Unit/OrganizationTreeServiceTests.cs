// ============================================================================
// US-CHR-006: Organization Tree / Hierarchy Visualization Unit Tests.
// Tests: department hierarchy tree build with correct parent-child + employee counts,
// lazy load by parentId, depth limiting, tenant isolation, inactive toggle,
// reporting view (best-effort: department managers with employees), empty states.
// Uses EF Core InMemory provider for lightweight database testing.
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

public sealed class OrganizationTreeServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OrganizationTreeService> _logger;

    public OrganizationTreeServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
        _logger = Substitute.For<ILogger<OrganizationTreeService>>();
    }

    private OrganizationTreeService CreateService(ITenantContext? ctx = null)
    {
        var dbContext = TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
        return new OrganizationTreeService(dbContext, ctx ?? _tenantContext, _logger);
    }

    // ── Seeding helpers ──────────────────────────────────────────────

    private async Task<Department> SeedDepartment(
        string name, string code,
        Guid? parentId = null,
        Guid? managerId = null,
        bool isActive = true,
        Guid? tenantId = null)
    {
        var tid = tenantId ?? _tenantId;
        var ctx = CreateTenantContext(tid);
        using var db = TestDbContextFactory.Create(ctx, _dbName);

        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            Name = name,
            Code = code,
            ParentDepartmentId = parentId,
            ManagerId = managerId,
            IsActive = isActive,
            IsDeleted = false,
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        return dept;
    }

    private async Task<(Employee Employee, Guid JobTitleId)> SeedEmployeeWithJobTitle(
        Guid departmentId,
        string firstName, string lastName, string email,
        string jobTitleName = "Software Engineer",
        bool isActive = true,
        Guid? tenantId = null)
    {
        var tid = tenantId ?? _tenantId;
        var ctx = CreateTenantContext(tid);
        using var db = TestDbContextFactory.Create(ctx, _dbName);

        // Create a job title if one doesn't exist for this name in the DB
        var jt = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            TitleName = jobTitleName,
            IsActive = true,
            IsDeleted = false,
        };
        db.JobTitles.Add(jt);

        var emp = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            EmployeeNo = $"EMP-{Guid.NewGuid().ToString()[..4]}",
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = departmentId,
            JobTitleId = jt.Id,
            EmploymentType = EmploymentType.FullTime,
            Status = isActive ? EmployeeStatus.Active : EmployeeStatus.Terminated,
            IsActive = isActive,
            IsDeleted = false,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return (emp, jt.Id);
    }

    private ITenantContext CreateTenantContext(Guid tenantId)
    {
        if (tenantId == _tenantId) return _tenantContext;
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    // ── Department view: hierarchy with correct parent-child ────────

    [Fact]
    public async Task DepartmentView_ThreeLevelHierarchy_ShouldBuildCorrectTree()
    {
        // Arrange: Company -> Engineering -> Backend
        var company = await SeedDepartment("Company", "COMP");
        var engineering = await SeedDepartment("Engineering", "ENG", parentId: company.Id);
        var backend = await SeedDepartment("Backend", "BE", parentId: engineering.Id);

        var service = CreateService();

        // Act
        var result = await service.GetDepartmentTreeAsync(parentId: null, depth: 3, includeInactive: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.View.Should().Be("department");
        result.Value.Nodes.Should().HaveCount(1, "only one root department");

        var root = result.Value.Nodes[0];
        root.NodeId.Should().Be(company.Id);
        root.NodeType.Should().Be("department");
        root.Name.Should().Be("Company");
        root.Children.Should().HaveCount(1);

        var eng = root.Children[0];
        eng.NodeId.Should().Be(engineering.Id);
        eng.Name.Should().Be("Engineering");
        eng.ChildrenCount.Should().Be(1);
        eng.Children.Should().HaveCount(1);

        var be = eng.Children[0];
        be.NodeId.Should().Be(backend.Id);
        be.Name.Should().Be("Backend");
        be.ChildrenCount.Should().Be(0);
        be.Children.Should().BeEmpty();
    }

    [Fact]
    public async Task DepartmentView_MultipleRoots_ShouldReturnAll()
    {
        var hr = await SeedDepartment("HR", "HR");
        var eng = await SeedDepartment("Engineering", "ENG");
        var finance = await SeedDepartment("Finance", "FIN");

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().HaveCount(3);
        result.Value.Nodes.Select(n => n.Name).Should()
            .Contain(new[] { "HR", "Engineering", "Finance" });
    }

    // ── Employee counts per department (FR-5) ───────────────────────

    [Fact]
    public async Task DepartmentView_ShouldShowCorrectEmployeeCounts()
    {
        var eng = await SeedDepartment("Engineering", "ENG");
        var hr = await SeedDepartment("HR", "HR");

        // Add 3 employees to Engineering, 1 to HR
        await SeedEmployeeWithJobTitle(eng.Id, "Alice", "A", "alice@t.com");
        await SeedEmployeeWithJobTitle(eng.Id, "Bob", "B", "bob@t.com");
        await SeedEmployeeWithJobTitle(eng.Id, "Charlie", "C", "charlie@t.com");
        await SeedEmployeeWithJobTitle(hr.Id, "Diana", "D", "diana@t.com");

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, false);

        result.IsSuccess.Should().BeTrue();
        var engNode = result.Value!.Nodes.First(n => n.Name == "Engineering");
        var hrNode = result.Value.Nodes.First(n => n.Name == "HR");

        engNode.EmployeeCount.Should().Be(3);
        hrNode.EmployeeCount.Should().Be(1);
    }

    [Fact]
    public async Task DepartmentView_InactiveEmployeesExcludedFromCount()
    {
        var eng = await SeedDepartment("Engineering", "ENG");
        await SeedEmployeeWithJobTitle(eng.Id, "Active", "E", "active@t.com", isActive: true);
        await SeedEmployeeWithJobTitle(eng.Id, "Inactive", "E", "inactive@t.com", isActive: false);

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, false);

        result.IsSuccess.Should().BeTrue();
        var engNode = result.Value!.Nodes.First();
        engNode.EmployeeCount.Should().Be(1, "inactive employees should not be counted");
    }

    // ── Lazy loading: parentId (FR-6, AC-5) ─────────────────────────

    [Fact]
    public async Task DepartmentView_LazyLoadByParentId_ShouldReturnOnlyChildren()
    {
        var root = await SeedDepartment("Root", "ROOT");
        var child1 = await SeedDepartment("Child1", "C1", parentId: root.Id);
        var child2 = await SeedDepartment("Child2", "C2", parentId: root.Id);
        var grandchild = await SeedDepartment("Grandchild", "GC", parentId: child1.Id);

        var service = CreateService();

        // Lazy load children of root
        var result = await service.GetDepartmentTreeAsync(parentId: root.Id, depth: 2, includeInactive: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().HaveCount(2, "root has 2 direct children");
        result.Value.Nodes.Select(n => n.Name).Should().Contain(new[] { "Child1", "Child2" });

        // Child1 should have Grandchild as a child
        var c1Node = result.Value.Nodes.First(n => n.Name == "Child1");
        c1Node.ChildrenCount.Should().Be(1);
        c1Node.Children.Should().HaveCount(1);
        c1Node.Children[0].Name.Should().Be("Grandchild");
    }

    [Fact]
    public async Task DepartmentView_LazyLoad_NonExistentParent_ShouldReturn404()
    {
        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(
            parentId: Guid.NewGuid(), depth: 2, includeInactive: false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("not found");
    }

    // ── Depth limiting ──────────────────────────────────────────────

    [Fact]
    public async Task DepartmentView_DepthLimit_ShouldTruncateTree()
    {
        var l1 = await SeedDepartment("Level1", "L1");
        var l2 = await SeedDepartment("Level2", "L2", parentId: l1.Id);
        var l3 = await SeedDepartment("Level3", "L3", parentId: l2.Id);
        var l4 = await SeedDepartment("Level4", "L4", parentId: l3.Id);

        var service = CreateService();

        // Depth 2: should see L1 -> L2, but L2 should NOT have L3 as a loaded child
        var result = await service.GetDepartmentTreeAsync(null, depth: 2, includeInactive: false);

        result.IsSuccess.Should().BeTrue();
        var root = result.Value!.Nodes[0];
        root.Name.Should().Be("Level1");
        root.Children.Should().HaveCount(1);

        var level2 = root.Children[0];
        level2.Name.Should().Be("Level2");
        level2.ChildrenCount.Should().Be(1, "L2 has L3 as a child");
        level2.Children.Should().BeEmpty("depth limit reached; children not loaded");
    }

    [Fact]
    public async Task DepartmentView_Depth1_ShouldReturnOnlyRoots()
    {
        var root = await SeedDepartment("Root", "ROOT");
        var child = await SeedDepartment("Child", "CH", parentId: root.Id);

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, depth: 1, includeInactive: false);

        result.IsSuccess.Should().BeTrue();
        var rootNode = result.Value!.Nodes[0];
        rootNode.Name.Should().Be("Root");
        rootNode.ChildrenCount.Should().Be(1);
        rootNode.Children.Should().BeEmpty("depth 1 means no children loaded");
    }

    // ── Inactive toggle (BR-4) ──────────────────────────────────────

    [Fact]
    public async Task DepartmentView_IncludeInactive_False_ShouldExcludeInactiveDepts()
    {
        var active = await SeedDepartment("Active Dept", "ACT", isActive: true);
        var inactive = await SeedDepartment("Inactive Dept", "INA", isActive: false);

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, includeInactive: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().HaveCount(1);
        result.Value.Nodes[0].Name.Should().Be("Active Dept");
    }

    [Fact]
    public async Task DepartmentView_IncludeInactive_True_ShouldIncludeAll()
    {
        var active = await SeedDepartment("Active Dept", "ACT", isActive: true);
        var inactive = await SeedDepartment("Inactive Dept", "INA", isActive: false);

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, includeInactive: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().HaveCount(2);
        var inactiveNode = result.Value.Nodes.First(n => n.Name == "Inactive Dept");
        inactiveNode.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DepartmentView_InactiveEmployeesIncludedInCountWhenToggled()
    {
        var eng = await SeedDepartment("Engineering", "ENG");
        await SeedEmployeeWithJobTitle(eng.Id, "Active", "E", "active@t.com", isActive: true);
        await SeedEmployeeWithJobTitle(eng.Id, "Terminated", "E", "term@t.com", isActive: false);

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, includeInactive: true);

        result.IsSuccess.Should().BeTrue();
        var engNode = result.Value!.Nodes.First();
        engNode.EmployeeCount.Should().Be(2, "includeInactive should count inactive employees too");
    }

    // ── Tenant isolation (FR-8, NFR-3) ──────────────────────────────

    [Fact]
    public async Task DepartmentView_TenantIsolation_ShouldNotLeakCrossTenantData()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedDepartment("Tenant A Dept", "TA", tenantId: tenantA);
        await SeedDepartment("Tenant B Dept", "TB", tenantId: tenantB);

        var ctxA = CreateTenantContext(tenantA);
        var serviceA = CreateService(ctxA);
        var resultA = await serviceA.GetDepartmentTreeAsync(null, 2, false);

        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.Nodes.Should().HaveCount(1);
        resultA.Value.Nodes[0].Name.Should().Be("Tenant A Dept");
    }

    [Fact]
    public async Task DepartmentView_TenantNotResolved_ShouldFail()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var service = CreateService(ctx);

        var result = await service.GetDepartmentTreeAsync(null, 2, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ── Department manager info on nodes ─────────────────────────────

    [Fact]
    public async Task DepartmentView_ShouldShowManagerNameOnDeptNode()
    {
        var eng = await SeedDepartment("Engineering", "ENG");
        var (manager, _) = await SeedEmployeeWithJobTitle(eng.Id, "Jane", "Manager", "jane@t.com");

        // Update department to set manager
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var dept = await db.Departments.FindAsync(eng.Id);
        dept!.ManagerId = manager.Id;
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, false);

        result.IsSuccess.Should().BeTrue();
        var engNode = result.Value!.Nodes.First(n => n.Name == "Engineering");
        engNode.Title.Should().Be("Jane Manager", "Title field shows manager name for department nodes");
    }

    // ── Reporting view (best-effort, US-CHR-011 deferred) ───────────

    [Fact]
    public async Task ReportingView_ShouldShowManagersWithReports()
    {
        var eng = await SeedDepartment("Engineering", "ENG");
        var (manager, _) = await SeedEmployeeWithJobTitle(eng.Id, "Jane", "Lead", "jane@t.com", "Tech Lead");
        var (emp1, _) = await SeedEmployeeWithJobTitle(eng.Id, "Alice", "Dev", "alice@t.com");
        var (emp2, _) = await SeedEmployeeWithJobTitle(eng.Id, "Bob", "Dev", "bob@t.com");

        // US-CHR-011: reports point to the manager via ReportsToEmployeeId
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var aliceRow = await db.Employees.FindAsync(emp1.Id);
        var bobRow = await db.Employees.FindAsync(emp2.Id);
        aliceRow!.ReportsToEmployeeId = manager.Id;
        bobRow!.ReportsToEmployeeId = manager.Id;
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.GetReportingTreeAsync(null, 2, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.View.Should().Be("reporting");
        result.Value.ReportingViewAvailable.Should().BeTrue("Employee.ReportsTo implemented in US-CHR-011");

        // Manager is the only root (no manager of their own); reports are children
        result.Value.Nodes.Should().HaveCount(1);
        var managerNode = result.Value.Nodes[0];
        managerNode.NodeType.Should().Be("employee");
        managerNode.Name.Should().Be("Jane Lead");

        // Manager's direct reports (excluding the manager themselves)
        managerNode.ChildrenCount.Should().Be(2);
        managerNode.Children.Should().HaveCount(2);
        managerNode.Children.Select(c => c.Name).Should()
            .Contain(new[] { "Alice Dev", "Bob Dev" });
    }

    [Fact]
    public async Task ReportingView_DepartmentWithoutManager_ShouldBeExcluded()
    {
        // Department has no manager assigned
        await SeedDepartment("Engineering", "ENG");

        var service = CreateService();
        var result = await service.GetReportingTreeAsync(null, 2, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().BeEmpty("departments without managers are excluded from reporting view");
    }

    [Fact]
    public async Task ReportingView_LazyLoadByManagerId_ShouldReturnReports()
    {
        var eng = await SeedDepartment("Engineering", "ENG");
        var (manager, _) = await SeedEmployeeWithJobTitle(eng.Id, "Jane", "Lead", "jane@t.com");
        var (emp1, _) = await SeedEmployeeWithJobTitle(eng.Id, "Alice", "Dev", "alice@t.com");

        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var aliceRow = await db.Employees.FindAsync(emp1.Id);
        aliceRow!.ReportsToEmployeeId = manager.Id;
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.GetReportingTreeAsync(parentId: manager.Id, depth: 2, includeInactive: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().HaveCount(1, "one direct report (not the manager)");
        result.Value.Nodes[0].Name.Should().Be("Alice Dev");
        result.Value.Nodes[0].ParentId.Should().Be(manager.Id);
    }

    [Fact]
    public async Task ReportingView_TenantNotResolved_ShouldFail()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var service = CreateService(ctx);

        var result = await service.GetReportingTreeAsync(null, 2, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ── Empty state ─────────────────────────────────────────────────

    [Fact]
    public async Task DepartmentView_NoDepartments_ShouldReturnEmptyNodes()
    {
        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().BeEmpty();
        result.Value.View.Should().Be("department");
    }

    [Fact]
    public async Task ReportingView_NoDepartments_ShouldReturnEmptyNodes()
    {
        var service = CreateService();
        var result = await service.GetReportingTreeAsync(null, 2, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Nodes.Should().BeEmpty();
        result.Value.View.Should().Be("reporting");
    }

    // ── Node data structure correctness ─────────────────────────────

    [Fact]
    public async Task DepartmentView_NodesShouldHaveCorrectTypes()
    {
        var dept = await SeedDepartment("Engineering", "ENG");
        await SeedEmployeeWithJobTitle(dept.Id, "John", "Doe", "john@t.com");

        var service = CreateService();
        var result = await service.GetDepartmentTreeAsync(null, 2, false);

        var node = result.Value!.Nodes[0];
        node.NodeType.Should().Be("department");
        node.NodeId.Should().Be(dept.Id);
        node.ParentId.Should().BeNull("root department has no parent");
        node.EmployeeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReportingView_EmployeeNodesShouldHaveCorrectFields()
    {
        var eng = await SeedDepartment("Engineering", "ENG");
        var (manager, _) = await SeedEmployeeWithJobTitle(eng.Id, "Jane", "Lead", "jane@t.com", "Tech Lead");
        var (emp, _) = await SeedEmployeeWithJobTitle(eng.Id, "Alice", "Dev", "alice@t.com", "Developer");

        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var aliceRow = await db.Employees.FindAsync(emp.Id);
        aliceRow!.ReportsToEmployeeId = manager.Id;
        await db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.GetReportingTreeAsync(null, 2, false);

        var managerNode = result.Value!.Nodes[0];
        managerNode.NodeType.Should().Be("employee");
        managerNode.EmployeeCount.Should().BeNull("employee nodes do not have employee counts");
        managerNode.ParentId.Should().BeNull("root-level manager node has no parent");

        var empNode = managerNode.Children[0];
        empNode.NodeType.Should().Be("employee");
        empNode.ParentId.Should().Be(manager.Id);
        empNode.ChildrenCount.Should().Be(0, "leaf employee has no direct reports");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
