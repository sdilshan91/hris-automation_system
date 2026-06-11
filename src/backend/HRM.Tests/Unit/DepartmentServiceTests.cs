// ============================================================================
// US-CHR-004: Department Management Unit Tests
// Tests department CRUD, name/code uniqueness (AC-2, AC-3), hierarchy/parent
// assignment (AC-4), cycle prevention (FR-5), deactivation rules (AC-5, BR-6),
// and cross-tenant isolation (NFR-2).
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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
        string name, string code, Guid? parentId = null, bool isActive = true, Guid? tenantId = null)
    {
        using var db = CreateDbContext();
        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId ?? _tenantId,
            Name = name,
            Code = code,
            ParentDepartmentId = parentId,
            IsActive = isActive,
            IsDeleted = false,
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        return dept.Id;
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
        var service = CreateService();
        var managerId = Guid.NewGuid(); // No FK constraint yet (deferred to US-CHR-001)

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
