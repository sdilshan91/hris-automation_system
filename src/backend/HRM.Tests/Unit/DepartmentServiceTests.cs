// ============================================================================
// US-CHR-004: Department Management Unit Tests
// Tests department CRUD, name/code uniqueness (AC-2, AC-3), hierarchy/parent
// assignment (AC-4), cycle prevention (FR-5), deactivation rules (AC-5, BR-6),
// and cross-tenant isolation (NFR-2).
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Departments.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class DepartmentServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DepartmentService> _logger;

    public DepartmentServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);

        _logger = Substitute.For<ILogger<DepartmentService>>();
    }

    private DepartmentService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new DepartmentService(dbContext, _tenantContext, _currentUser, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task<Guid> SeedDepartment(
        string name, string code, Guid? parentId = null, bool isActive = true, Guid? tenantId = null,
        Guid? managerId = null)
    {
        using var db = CreateDbContext();
        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId ?? _tenantId,
            Name = name,
            Code = code,
            ManagerId = managerId,
            ParentDepartmentId = parentId,
            IsActive = isActive,
            IsDeleted = false,
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        return dept.Id;
    }

    /// <summary>
    /// Seeds an active employee in the current tenant so managerId assignment can be
    /// exercised against a real, same-tenant employee (BUG-014 fix requires the manager
    /// to be an existing same-tenant employee). EmploymentType/Status use entity defaults.
    /// </summary>
    private async Task<Guid> SeedEmployee(
        string firstName, string lastName, Guid? tenantId = null,
        Guid? departmentId = null, bool isActive = true)
    {
        using var db = CreateDbContext();
        var emp = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId ?? _tenantId,
            EmployeeNo = $"EMP-{Guid.NewGuid().ToString()[..4]}",
            FirstName = firstName,
            LastName = lastName,
            Email = $"{firstName}.{lastName}@test.com".ToLowerInvariant(),
            DepartmentId = departmentId ?? BaseEntity.NewUuidV7(),
            JobTitleId = BaseEntity.NewUuidV7(),
            DateOfJoining = DateTime.UtcNow.AddYears(-1),
            IsActive = isActive,
            IsDeleted = false,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    // ── AC-2: Create department ──────────────────────────────────────

    [Fact]
    public async Task Create_ValidRootDepartment_ShouldSucceed()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            "Engineering", "ENG", "Engineering department", null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Engineering");
        result.Value.Code.Should().Be("ENG");
        result.Value.Description.Should().Be("Engineering department");
        result.Value.ParentDepartmentId.Should().BeNull();
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Create_ValidChildDepartment_ShouldSucceed()
    {
        // Arrange: create parent first
        var parentId = await SeedDepartment("Engineering", "ENG");
        var service = CreateService();

        // Act
        var result = await service.CreateAsync(
            "Backend Team", "ENG-BE", "Backend engineering", parentId, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ParentDepartmentId.Should().Be(parentId);
        result.Value.ParentDepartmentName.Should().Be("Engineering");
    }

    [Fact]
    public async Task Create_WithManagerId_ShouldSucceed()
    {
        // BUG-014: managerId must be an existing same-tenant employee. Seed a real
        // active employee in this tenant rather than a random Guid so the assignment
        // is valid under the tenant-scoped manager validation.
        var managerId = await SeedEmployee("Jane", "Smith");
        var service = CreateService();

        var result = await service.CreateAsync(
            "Sales", "SALES", null, null, managerId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ManagerId.Should().Be(managerId);
    }

    // ── AC-3: Duplicate name rejection ───────────────────────────────

    [Fact]
    public async Task Create_DuplicateNameSameTenant_ShouldFail()
    {
        await SeedDepartment("Engineering", "ENG");
        var service = CreateService();

        var result = await service.CreateAsync(
            "Engineering", "ENG2", null, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A department with this name already exists.");
    }

    // ── BUG-013: case-insensitive name uniqueness ───────────────────
    // Regression for the case-insensitive uniqueness cluster. Pre-fix the duplicate
    // check compared `d.Name == name` (case-sensitive), so a case-variant slipped past
    // and a second row persisted. Post-fix the check is `d.Name.ToLower() == name.Trim().ToLower()`.

    [Fact]
    public async Task CreateDepartment_CaseVariantName_IsRejected_BUG013()
    {
        // Arrange: a department "Engineering" already exists in this tenant.
        await SeedDepartment("Engineering", "ENG");
        var service = CreateService();

        // Act: attempt to create a case-variant "engineering" (distinct code so only
        // the name check can reject it).
        var result = await service.CreateAsync("engineering", "ENG2", null, null, null);

        // Assert: rejected as a duplicate (pre-fix this SUCCEEDED — the bug).
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A department with this name already exists.");

        // And no second row persisted — still exactly one department in the tenant.
        using var db = CreateDbContext();
        db.Departments.Count().Should().Be(1);
    }

    [Fact]
    public async Task UpdateDepartment_CaseVariantOfAnother_IsRejected_BUG013()
    {
        // Arrange: two departments in this tenant.
        await SeedDepartment("Engineering", "ENG");
        var hrId = await SeedDepartment("HR", "HR");
        var service = CreateService();

        // Act: rename HR to a case-variant of the existing "Engineering".
        var result = await service.UpdateAsync(hrId, "ENGINEERING", "HR", null, null, null);

        // Assert: rejected (excluding-self clause must be case-insensitive too).
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A department with this name already exists.");
    }

    [Fact]
    public async Task CreateDepartment_GenuinelyDistinctName_Succeeds_BUG013()
    {
        // Positive control: a truly different name still creates (the fix must not
        // over-reject). Guards against a tautological "always rejects" test.
        await SeedDepartment("Engineering", "ENG");
        var service = CreateService();

        var result = await service.CreateAsync("Marketing", "MKT", null, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Marketing");

        using var db = CreateDbContext();
        db.Departments.Count().Should().Be(2);
    }

    [Fact]
    public async Task Create_DuplicateCodeSameTenant_ShouldFail()
    {
        await SeedDepartment("Engineering", "ENG");
        var service = CreateService();

        var result = await service.CreateAsync(
            "Human Resources", "ENG", null, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("code");
    }

    [Fact]
    public async Task Create_SameNameDifferentTenant_ShouldSucceed()
    {
        // Arrange: create department in tenant A
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed in tenant A using a different tenant context
        var tenantAContext = Substitute.For<ITenantContext>();
        tenantAContext.TenantId.Returns(tenantA);
        tenantAContext.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(tenantAContext, _dbName);
        dbA.Departments.Add(new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            Name = "Engineering",
            Code = "ENG",
            IsActive = true,
        });
        await dbA.SaveChangesAsync();

        // Act: create same name in tenant B
        var tenantBContext = Substitute.For<ITenantContext>();
        tenantBContext.TenantId.Returns(tenantB);
        tenantBContext.IsResolved.Returns(true);
        var dbB = TestDbContextFactory.Create(tenantBContext, _dbName);
        var serviceB = new DepartmentService(
            dbB, tenantBContext, _currentUser, _logger);

        var result = await serviceB.CreateAsync(
            "Engineering", "ENG", null, null, null);

        // Assert: same name succeeds in a different tenant (BR-1)
        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-4: Update and hierarchy changes ───────────────────────────

    [Fact]
    public async Task Update_ChangeParent_ShouldSucceed()
    {
        var engId = await SeedDepartment("Engineering", "ENG");
        var hrId = await SeedDepartment("HR", "HR");
        var backendId = await SeedDepartment("Backend", "BE", engId);
        var service = CreateService();

        // Move Backend from Engineering to HR
        var result = await service.UpdateAsync(
            backendId, "Backend", "BE", null, hrId, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ParentDepartmentId.Should().Be(hrId);
        result.Value.ParentDepartmentName.Should().Be("HR");
    }

    [Fact]
    public async Task Update_ChangeName_ShouldSucceed()
    {
        var id = await SeedDepartment("Old Name", "OLD");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "New Name", "OLD", "updated description", null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task Update_DuplicateNameExcludingSelf_ShouldFail()
    {
        var id1 = await SeedDepartment("Engineering", "ENG");
        var id2 = await SeedDepartment("HR", "HR");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id2, "Engineering", "HR", null, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A department with this name already exists.");
    }

    [Fact]
    public async Task Update_SameNameAsSelf_ShouldSucceed()
    {
        var id = await SeedDepartment("Engineering", "ENG");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "Engineering", "ENG", "updated description", null, null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_NonExistentDepartment_ShouldFail()
    {
        var service = CreateService();

        var result = await service.UpdateAsync(
            Guid.NewGuid(), "DoesNotExist", "DNE", null, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── FR-5: Circular reference prevention ──────────────────────────

    [Fact]
    public async Task Update_SelfAsParent_ShouldFail()
    {
        var id = await SeedDepartment("Engineering", "ENG");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "Engineering", "ENG", null, id, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("cannot be its own parent");
    }

    [Fact]
    public async Task Update_DirectCircularReference_ShouldFail()
    {
        // A -> B; try to set A's parent to B
        var aId = await SeedDepartment("A", "A");
        var bId = await SeedDepartment("B", "B", aId);
        var service = CreateService();

        var result = await service.UpdateAsync(
            aId, "A", "A", null, bId, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Circular reference");
    }

    [Fact]
    public async Task Update_DeepCircularReference_ShouldFail()
    {
        // A -> B -> C; try to set A's parent to C
        var aId = await SeedDepartment("A", "A");
        var bId = await SeedDepartment("B", "B", aId);
        var cId = await SeedDepartment("C", "C", bId);
        var service = CreateService();

        var result = await service.UpdateAsync(
            aId, "A", "A", null, cId, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Circular reference");
    }

    [Fact]
    public async Task Update_ValidParentChange_ShouldSucceed()
    {
        // A, B, C (all roots); move C under B - no cycle
        var aId = await SeedDepartment("A", "A");
        var bId = await SeedDepartment("B", "B");
        var cId = await SeedDepartment("C", "C");
        var service = CreateService();

        var result = await service.UpdateAsync(
            cId, "C", "C", null, bId, null);

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-5 / BR-6: Deactivate rules ───────────────────────────────

    [Fact]
    public async Task Deactivate_DepartmentWithNoChildren_ShouldSucceed()
    {
        var id = await SeedDepartment("Marketing", "MKT");
        var service = CreateService();

        var result = await service.DeactivateAsync(id);

        result.IsSuccess.Should().BeTrue();

        // Verify deactivation
        using var db = CreateDbContext();
        // The global query filter excludes soft-deleted items, but IsActive = false
        // is still accessible through the filter (filter is on IsDeleted, not IsActive)
        var dept = db.Departments.FirstOrDefault(d => d.Id == id);
        dept.Should().NotBeNull();
        dept!.IsActive.Should().BeFalse();
    }

    // ── ISSUE-020: deactivate emits a DISTINCT Department.Deactivated audit action ──

    [Fact]
    [Trait("TC", "TC-CHR-031")]
    public async Task Deactivate_WritesDistinctDepartmentDeactivatedAuditAction_ISSUE020()
    {
        var id = await SeedDepartment("Marketing", "MKT");

        var result = await CreateService().DeactivateAsync(id);
        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        // A DISTINCT semantic action must exist so the trail is queryable by "deactivated" — pre-fix the only
        // row was the generic Department.Update field diff.
        var audit = db.AuditLogs
            .Where(a => a.Action == "Department.Deactivated" && a.ResourceId == id.ToString())
            .ToList();

        audit.Should().ContainSingle("deactivate must write a distinct Department.Deactivated audit row");
        var row = audit[0];
        row.EventType.Should().Be("Department.Deactivated");
        row.ResourceType.Should().Be("Department");
        row.UserId.Should().Be(_currentUser.UserId);
        row.TenantId.Should().Be(_tenantId);
        row.Before.Should().Contain("true", "the before-snapshot records IsActive:true");
        row.After.Should().Contain("false", "the after-snapshot records IsActive:false");
    }

    [Fact]
    public async Task Deactivate_DepartmentWithActiveChildren_ShouldFail()
    {
        var parentId = await SeedDepartment("Engineering", "ENG");
        await SeedDepartment("Backend", "BE", parentId);
        var service = CreateService();

        var result = await service.DeactivateAsync(parentId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("active child department");
    }

    [Fact]
    public async Task Deactivate_DepartmentWithInactiveChildren_ShouldSucceed()
    {
        var parentId = await SeedDepartment("Engineering", "ENG");
        await SeedDepartment("Deprecated Team", "DT", parentId, isActive: false);
        var service = CreateService();

        var result = await service.DeactivateAsync(parentId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivated_ShouldFail()
    {
        var id = await SeedDepartment("Old Dept", "OLD", isActive: false);
        var service = CreateService();

        var result = await service.DeactivateAsync(id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already deactivated");
    }

    [Fact]
    public async Task Deactivate_NonExistentDepartment_ShouldFail()
    {
        var service = CreateService();

        var result = await service.DeactivateAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Cross-tenant isolation (NFR-2) ───────────────────────────────

    [Fact]
    public async Task GetAll_ShouldOnlyReturnCurrentTenantDepartments()
    {
        // Arrange: seed departments in two different tenants
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed in tenant A
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        dbA.Departments.Add(new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            Name = "Tenant A Engineering",
            Code = "A-ENG",
            IsActive = true,
        });
        await dbA.SaveChangesAsync();

        // Seed in tenant B
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var dbB = TestDbContextFactory.Create(ctxB, _dbName);
        dbB.Departments.Add(new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantB,
            Name = "Tenant B Engineering",
            Code = "B-ENG",
            IsActive = true,
        });
        await dbB.SaveChangesAsync();

        // Act: query from tenant A
        var serviceA = new DepartmentService(
            TestDbContextFactory.Create(ctxA, _dbName), ctxA, _currentUser, _logger);
        var resultA = await serviceA.GetAllAsync();

        // Assert: tenant A sees only their own departments
        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.Should().HaveCount(1);
        resultA.Value[0].Name.Should().Be("Tenant A Engineering");

        // Act: query from tenant B
        var serviceB = new DepartmentService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);
        var resultB = await serviceB.GetAllAsync();

        // Assert: tenant B sees only their own departments
        resultB.IsSuccess.Should().BeTrue();
        resultB.Value!.Should().HaveCount(1);
        resultB.Value[0].Name.Should().Be("Tenant B Engineering");
    }

    [Fact]
    public async Task GetById_CrossTenant_ShouldReturn404()
    {
        // Arrange: department in tenant A
        var tenantA = Guid.NewGuid();
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        var deptA = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            Name = "Secret Dept",
            Code = "SEC",
            IsActive = true,
        };
        dbA.Departments.Add(deptA);
        await dbA.SaveChangesAsync();

        // Act: try to access from tenant B
        var tenantB = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new DepartmentService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);

        var result = await serviceB.GetByIdAsync(deptA.Id);

        // Assert: not found (tenant isolation)
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Hierarchy / tree queries ─────────────────────────────────────

    [Fact]
    public async Task GetTree_ShouldReturnHierarchy()
    {
        var engId = await SeedDepartment("Engineering", "ENG");
        await SeedDepartment("Backend", "BE", engId);
        await SeedDepartment("Frontend", "FE", engId);
        await SeedDepartment("HR", "HR");
        var service = CreateService();

        var result = await service.GetTreeAsync();

        result.IsSuccess.Should().BeTrue();
        // Two root nodes: Engineering and HR
        result.Value!.Should().HaveCount(2);

        var engineering = result.Value.First(n => n.Name == "Engineering");
        engineering.Children.Should().HaveCount(2);
        engineering.Children.Select(c => c.Name).Should().Contain("Backend");
        engineering.Children.Select(c => c.Name).Should().Contain("Frontend");

        var hr = result.Value.First(n => n.Name == "HR");
        hr.Children.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTree_ShouldExcludeInactiveDepartments()
    {
        var engId = await SeedDepartment("Engineering", "ENG");
        await SeedDepartment("Active Team", "AT", engId);
        await SeedDepartment("Inactive Team", "IT", engId, isActive: false);
        var service = CreateService();

        var result = await service.GetTreeAsync();

        result.IsSuccess.Should().BeTrue();
        var engineering = result.Value!.First(n => n.Name == "Engineering");
        engineering.Children.Should().HaveCount(1);
        engineering.Children[0].Name.Should().Be("Active Team");
    }

    // ── GetAll with filter ───────────────────────────────────────────

    [Fact]
    public async Task GetAll_ActiveOnly_ShouldFilterInactive()
    {
        await SeedDepartment("Active", "ACT");
        await SeedDepartment("Inactive", "INACT", isActive: false);
        var service = CreateService();

        var result = await service.GetAllAsync(activeOnly: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value[0].Name.Should().Be("Active");
    }

    [Fact]
    public async Task GetAll_NoFilter_ShouldReturnAll()
    {
        await SeedDepartment("Active", "ACT");
        await SeedDepartment("Inactive", "INACT", isActive: false);
        var service = CreateService();

        var result = await service.GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    // ── ISSUE-364: EmployeeCount + ManagerName ──────────────────────────────────────────────────────
    // DepartmentDto returned neither, while the FE model invented both — so the list rendered "undefined
    // employees", the manager line was permanently blank, and the deactivate dialog's active-employee
    // warning could never fire (`undefined > 0` is false). Both sibling DTOs (JobTitle, Location) already
    // returned a count, which is what made departments the inconsistent one.

    [Fact]
    public async Task GetAll_returns_the_ACTIVE_employee_count_per_department_issue364()
    {
        var deptId = await SeedDepartment("Engineering", "ENG");
        await SeedEmployee("Ann", "One", departmentId: deptId);
        await SeedEmployee("Bob", "Two", departmentId: deptId);
        await SeedEmployee("Ex", "Employee", departmentId: deptId, isActive: false);   // must NOT count
        await SeedEmployee("Other", "Dept");                                            // different department

        var result = await service_GetAll();

        var eng = result.Single(d => d.Code == "ENG");
        eng.EmployeeCount.Should().Be(2, "only ACTIVE employees of THIS department count");
    }

    [Fact]
    public async Task GetAll_returns_the_manager_display_name_issue364()
    {
        var managerId = await SeedEmployee("Jane", "Smith");
        await SeedDepartment("Managed", "MGD", managerId: managerId);

        var result = await service_GetAll();

        result.Single(d => d.Code == "MGD").ManagerName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task GetAll_leaves_manager_name_null_when_no_manager_is_set_issue364()
    {
        await SeedDepartment("Unmanaged", "UNM");

        var result = await service_GetAll();

        result.Single(d => d.Code == "UNM").ManagerName.Should().BeNull();
        result.Single(d => d.Code == "UNM").EmployeeCount.Should().Be(0);
    }

    private async Task<IReadOnlyList<DepartmentDto>> service_GetAll()
    {
        var result = await CreateService().GetAllAsync();
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    // ── Tenant context not resolved ──────────────────────────────────

    [Fact]
    public async Task Create_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.CreateAsync("Eng", "ENG", null, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    [Fact]
    public async Task GetAll_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.GetAllAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ── Parent validation ────────────────────────────────────────────

    [Fact]
    public async Task Create_WithInactiveParent_ShouldFail()
    {
        var parentId = await SeedDepartment("Inactive Parent", "IP", isActive: false);
        var service = CreateService();

        var result = await service.CreateAsync(
            "Child", "CH", null, parentId, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Parent department not found or is not active");
    }

    [Fact]
    public async Task Create_WithNonExistentParent_ShouldFail()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            "Child", "CH", null, Guid.NewGuid(), null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Parent department not found");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
