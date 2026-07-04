// ============================================================================
// Regression tests for BUG-014 (HIGH, Core HR, US-CHR-004, TC-CHR-020).
//
// Bug: DepartmentService create/update writes `ManagerId` with NO same-tenant
// validation. Unlike `parentDepartmentId` (validated via a tenant-scoped query),
// a Tenant Admin could set ANOTHER tenant's employee as their department's
// manager and it persisted (HTTP 200). The fix adds a tenant-scoped existence
// check on `managerId` (mirroring the parent-department validation, BR-2 / FR-4):
// a managerId that is not a same-tenant employee must be rejected with 400.
//
// Why these tests genuinely fail pre-fix and pass post-fix:
//   The two-tenant scenario is seeded in one shared EF InMemory database. The
//   trigger employee belongs to tenant B; the department update runs under
//   tenant A. EF InMemory honours the global query filter on Employee
//   (AppDbContext.OnModelCreating: e.TenantId == _tenantContext.TenantId), so
//   the fix's tenant-scoped manager lookup finds NOTHING under tenant A and
//   returns 400. Pre-fix, no lookup exists and InMemory enforces no FK, so the
//   cross-tenant managerId is written and the update wrongly succeeds (200) ->
//   the trigger test fails, exactly reproducing BUG-014.
//   The same-tenant control passes both pre- and post-fix, proving the fix does
//   not over-restrict legitimate assignments.
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

public sealed class DepartmentManagerTenantValidationTests : IDisposable
{
    // Two distinct tenants share ONE InMemory database (keyed by _dbName) so a
    // tenant-B employee physically exists while the service runs under tenant A.
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DepartmentService> _logger;
    private int _empCounter;

    public DepartmentManagerTenantValidationTests()
    {
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("tenantadmin@acme.test");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<DepartmentService>>();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private ITenantContext ContextFor(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private DepartmentService ServiceFor(Guid tenantId)
    {
        var ctx = ContextFor(tenantId);
        var db = TestDbContextFactory.Create(ctx, _dbName);
        return new DepartmentService(db, ctx, _currentUser, _logger);
    }

    /// <summary>
    /// Seeds a real, ACTIVE employee in the given tenant. The trigger and the
    /// control employees are identical in every respect EXCEPT their tenant, so
    /// the assertion keys purely on the tenant mismatch (not on a missing/invalid id).
    /// </summary>
    private async Task<Guid> SeedEmployeeAsync(Guid tenantId, string firstName, string lastName)
    {
        var ctx = ContextFor(tenantId);
        using var db = TestDbContextFactory.Create(ctx, _dbName);
        var emp = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeNo = $"EMP-{++_empCounter:D4}",
            FirstName = firstName,
            LastName = lastName,
            Email = $"{firstName}.{lastName}.{_empCounter}@t.test".ToLowerInvariant(),
            DepartmentId = BaseEntity.NewUuidV7(),
            JobTitleId = BaseEntity.NewUuidV7(),
            DateOfJoining = DateTime.UtcNow.AddYears(-1),
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            IsDeleted = false,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    private async Task<Guid> SeedDepartmentAsync(
        Guid tenantId, string name, string code, Guid? managerId = null)
    {
        var ctx = ContextFor(tenantId);
        using var db = TestDbContextFactory.Create(ctx, _dbName);
        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            Name = name,
            Code = code,
            ManagerId = managerId,
            IsActive = true,
            IsDeleted = false,
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        return dept.Id;
    }

    private async Task<Guid?> ReadPersistedManagerIdAsync(Guid tenantId, Guid departmentId)
    {
        var ctx = ContextFor(tenantId);
        using var db = TestDbContextFactory.Create(ctx, _dbName);
        var dept = db.Departments.Single(d => d.Id == departmentId);
        return dept.ManagerId;
    }

    // ── TC-CHR-020 trigger: cross-tenant manager must be rejected ─────

    [Fact]
    public async Task Update_CrossTenantManagerId_IsRejected() // BUG-014
    {
        // Arrange: an ACTIVE employee that belongs to tenant B (a real, existing row).
        var tenantBManagerId = await SeedEmployeeAsync(_tenantBId, "Foreign", "Manager");

        // A tenant-A department that currently has a legitimate same-tenant manager,
        // so we can prove the cross-tenant attempt does NOT overwrite it.
        var tenantAManagerId = await SeedEmployeeAsync(_tenantAId, "Home", "Manager");
        var deptId = await SeedDepartmentAsync(
            _tenantAId, "Engineering", "ENG", managerId: tenantAManagerId);

        var serviceA = ServiceFor(_tenantAId);

        // Act: as tenant A, try to set tenant B's employee as the department manager.
        var result = await serviceA.UpdateAsync(
            deptId, "Engineering", "ENG", null, null, tenantBManagerId);

        // Assert: rejected (BR-2 / FR-4) — pre-fix this WRONGLY returns success.
        result.IsFailure.Should().BeTrue(
            "a managerId belonging to another tenant must not be accepted (BUG-014)");
        result.StatusCode.Should().Be(400);

        // And the department's manager_id is NOT updated — the original same-tenant
        // manager is preserved (no cross-tenant FK written).
        var persisted = await ReadPersistedManagerIdAsync(_tenantAId, deptId);
        persisted.Should().Be(
            tenantAManagerId,
            "the rejected cross-tenant update must leave manager_id unchanged");
        persisted.Should().NotBe(tenantBManagerId);
    }

    // ── TC-CHR-020 control: same-tenant manager still succeeds ────────

    [Fact]
    public async Task Update_SameTenantManagerId_Succeeds() // BUG-014 control
    {
        // Arrange: an ACTIVE employee in the SAME tenant (A) as the department.
        var tenantAManagerId = await SeedEmployeeAsync(_tenantAId, "Jane", "Smith");
        var deptId = await SeedDepartmentAsync(_tenantAId, "Engineering", "ENG");

        var serviceA = ServiceFor(_tenantAId);

        // Act: assign the same-tenant employee as manager.
        var result = await serviceA.UpdateAsync(
            deptId, "Engineering", "ENG", null, null, tenantAManagerId);

        // Assert: succeeds and is persisted (fix must not over-restrict).
        result.IsSuccess.Should().BeTrue(
            "a same-tenant employee is a valid department manager");
        result.Value!.ManagerId.Should().Be(tenantAManagerId);

        var persisted = await ReadPersistedManagerIdAsync(_tenantAId, deptId);
        persisted.Should().Be(tenantAManagerId);
    }

    // ── Optional: clearing the manager (null) stays allowed ───────────

    [Fact]
    public async Task Update_NullManagerId_IsAllowed() // BUG-014 — clearing still permitted
    {
        var tenantAManagerId = await SeedEmployeeAsync(_tenantAId, "Bob", "Wilson");
        var deptId = await SeedDepartmentAsync(
            _tenantAId, "Engineering", "ENG", managerId: tenantAManagerId);

        var serviceA = ServiceFor(_tenantAId);

        // Act: clear the manager.
        var result = await serviceA.UpdateAsync(
            deptId, "Engineering", "ENG", null, null, null);

        // Assert: allowed; manager_id becomes null.
        result.IsSuccess.Should().BeTrue("clearing the manager (null) must remain allowed");
        result.Value!.ManagerId.Should().BeNull();

        var persisted = await ReadPersistedManagerIdAsync(_tenantAId, deptId);
        persisted.Should().BeNull();
    }

    // ── Create path shares the same missing-validation root ───────────

    [Fact]
    public async Task Create_CrossTenantManagerId_IsRejected() // BUG-014 (create arm)
    {
        // A real, active tenant-B employee.
        var tenantBManagerId = await SeedEmployeeAsync(_tenantBId, "Foreign", "Boss");

        var serviceA = ServiceFor(_tenantAId);

        // Act: as tenant A, create a department whose manager is tenant B's employee.
        var result = await serviceA.CreateAsync(
            "Sales", "SALES", null, null, tenantBManagerId);

        // Assert: rejected — pre-fix this WRONGLY succeeds and persists the FK.
        result.IsFailure.Should().BeTrue(
            "creating a department with another tenant's employee as manager must be rejected (BUG-014)");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Create_SameTenantManagerId_Succeeds() // BUG-014 (create control)
    {
        var tenantAManagerId = await SeedEmployeeAsync(_tenantAId, "Alice", "Anders");

        var serviceA = ServiceFor(_tenantAId);

        var result = await serviceA.CreateAsync(
            "Sales", "SALES", null, null, tenantAManagerId);

        result.IsSuccess.Should().BeTrue("a same-tenant employee is a valid manager on create");
        result.Value!.ManagerId.Should().Be(tenantAManagerId);
    }

    public void Dispose()
    {
        // InMemory databases are released when the last context is disposed.
    }
}
