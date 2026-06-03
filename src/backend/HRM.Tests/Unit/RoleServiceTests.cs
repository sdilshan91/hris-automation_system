// ============================================================================
// US-AUTH-006: RBAC Unit Tests - RoleService
// Tests built-in role protection (FR-2, AC-6), tenant owner protection (FR-8, BR-6),
// system role isolation (FR-9), role CRUD, and user role assignment.
// Uses EF Core InMemory provider for a lightweight database substitute.
//
// Follow-up: Integration tests with Testcontainers + PostgreSQL are NOT included.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Caching;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RoleServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionCache _permissionCache;
    private readonly ILogger<RoleService> _logger;

    public RoleServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _permissionCache = new InMemoryPermissionCache();
        _logger = Substitute.For<ILogger<RoleService>>();
    }

    private RoleService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new RoleService(dbContext, _tenantContext, _currentUser, _permissionCache, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task SeedBuiltInRole(string roleName, bool isBuiltIn = true, IReadOnlyList<string>? permissions = null)
    {
        using var db = CreateDbContext();
        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = roleName,
            IsBuiltIn = isBuiltIn,
            CreatedAt = DateTime.UtcNow,
        };

        if (permissions is not null)
        {
            foreach (var perm in permissions)
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = perm });
        }

        db.Roles.Add(role);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCustomRole(string name, IReadOnlyList<string>? permissions = null)
    {
        using var db = CreateDbContext();
        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = name,
            IsBuiltIn = false,
            CreatedAt = DateTime.UtcNow,
        };

        if (permissions is not null)
        {
            foreach (var perm in permissions)
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = perm });
        }

        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private async Task<(Guid userTenantId, Guid userId)> SeedUserTenantWithRoles(params Guid[] roleIds)
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "user@test.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);

        var userTenant = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        db.UserTenants.Add(userTenant);

        foreach (var roleId in roleIds)
        {
            db.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = userTenant.Id,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = "test",
            });
        }

        await db.SaveChangesAsync();
        return (userTenant.Id, userId);
    }

    // ── Built-in Role Protection (FR-2, AC-6) ────────────────────────

    [Fact]
    public async Task DeleteRole_BuiltInRole_ShouldReturnFailure()
    {
        // Arrange
        await SeedBuiltInRole("Tenant Owner");
        using var db = CreateDbContext();
        var role = db.Roles.First(r => r.Name == "Tenant Owner");
        var service = CreateService();

        // Act
        var result = await service.DeleteRoleAsync(role.Id);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Built-in roles cannot be deleted.");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateRole_BuiltInRole_ShouldReturnFailure()
    {
        // Arrange
        await SeedBuiltInRole("HR Officer");
        using var db = CreateDbContext();
        var role = db.Roles.First(r => r.Name == "HR Officer");
        var service = CreateService();

        // Act
        var result = await service.UpdateRoleAsync(role.Id, "Modified Name", null, new[] { "Leave.Apply" });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Built-in roles cannot be modified.");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task DeleteRole_CustomRole_ShouldSucceed()
    {
        // Arrange
        var roleId = await SeedCustomRole("Custom Analyst", new[] { "Reports.View" });
        var service = CreateService();

        // Act
        var result = await service.DeleteRoleAsync(roleId);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify deleted
        using var db = CreateDbContext();
        db.Roles.Any(r => r.Id == roleId).Should().BeFalse();
    }

    // ── Create Role ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateRole_ValidInput_ShouldSucceed()
    {
        var service = CreateService();

        var result = await service.CreateRoleAsync(
            "Custom HR Lead",
            "Leads HR operations",
            new[] { "Employee.View.All", "Leave.Approve.All" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Custom HR Lead");
        result.Value.IsBuiltIn.Should().BeFalse();
        result.Value.Permissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateRole_DuplicateName_ShouldFail()
    {
        await SeedCustomRole("Existing Role");
        var service = CreateService();

        var result = await service.CreateRoleAsync("Existing Role", null, Array.Empty<string>());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateRole_SystemRoleName_ShouldFail()
    {
        // FR-9: System roles cannot be created in regular tenants
        var service = CreateService();

        var result = await service.CreateRoleAsync("System Super Admin", "Hack attempt", Array.Empty<string>());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("System roles cannot be created");
    }

    // ── Update Role ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRole_CustomRole_ShouldSucceed()
    {
        var roleId = await SeedCustomRole("Outdated Name", new[] { "Reports.View" });
        var service = CreateService();

        var result = await service.UpdateRoleAsync(
            roleId,
            "Updated Name",
            "Updated description",
            new[] { "Reports.View", "Reports.Export" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Updated Name");
        result.Value.Permissions.Should().HaveCount(2);
    }

    // ── Tenant Owner Protection (FR-8, BR-6) ─────────────────────────

    [Fact]
    public async Task AssignUserRoles_RemoveSoleTenantOwner_ShouldFail()
    {
        // Arrange: create Tenant Owner role and assign to a single user
        await SeedBuiltInRole("Tenant Owner");
        using var seedDb = CreateDbContext();
        var ownerRole = seedDb.Roles.First(r => r.Name == "Tenant Owner");

        var otherRoleId = await SeedCustomRole("Developer", new[] { "Employee.View.Own" });
        var (userTenantId, _) = await SeedUserTenantWithRoles(ownerRole.Id);

        var service = CreateService();

        // Act: try to replace the owner role with just "Developer"
        var result = await service.AssignUserRolesAsync(userTenantId, new[] { otherRoleId });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Cannot remove the Tenant Owner role from the sole owner");
    }

    [Fact]
    public async Task AssignUserRoles_RemoveTenantOwner_WhenOtherOwnerExists_ShouldSucceed()
    {
        // Arrange: two users have Tenant Owner
        await SeedBuiltInRole("Tenant Owner");
        using var seedDb = CreateDbContext();
        var ownerRole = seedDb.Roles.First(r => r.Name == "Tenant Owner");

        var otherRoleId = await SeedCustomRole("Developer", new[] { "Employee.View.Own" });

        // First user (the one we'll remove from)
        var (userTenantId1, _) = await SeedUserTenantWithRoles(ownerRole.Id);
        // Second user (keeps the owner role)
        await SeedUserTenantWithRoles(ownerRole.Id);

        var service = CreateService();

        // Act: remove owner from user 1
        var result = await service.AssignUserRolesAsync(userTenantId1, new[] { otherRoleId });

        // Assert: should succeed because there is still another owner
        result.IsSuccess.Should().BeTrue();
    }

    // ── Get Roles ────────────────────────────────────────────────────

    [Fact]
    public async Task GetRoles_ShouldReturnAllTenantRoles()
    {
        await SeedBuiltInRole("Manager");
        await SeedCustomRole("Reviewer");
        var service = CreateService();

        var result = await service.GetRolesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetRoleById_ExistingRole_ShouldReturnRole()
    {
        var roleId = await SeedCustomRole("Test Role", new[] { "Leave.Apply" });
        var service = CreateService();

        var result = await service.GetRoleByIdAsync(roleId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Test Role");
        result.Value.Permissions.Should().Contain("Leave.Apply");
    }

    [Fact]
    public async Task GetRoleById_NonExistentRole_ShouldFail()
    {
        var service = CreateService();

        var result = await service.GetRoleByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Role Assignment ──────────────────────────────────────────────

    [Fact]
    public async Task AssignUserRoles_ValidInput_ShouldSucceed()
    {
        var roleId = await SeedCustomRole("Dev Lead", new[] { "Employee.View.Team" });
        var (userTenantId, _) = await SeedUserTenantWithRoles();

        var service = CreateService();
        var result = await service.AssignUserRolesAsync(userTenantId, new[] { roleId });

        result.IsSuccess.Should().BeTrue();

        // Verify assignment
        using var db = CreateDbContext();
        db.UserTenantRoles.Any(utr => utr.UserTenantId == userTenantId && utr.RoleId == roleId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task AssignUserRoles_InvalidUserTenantId_ShouldFail()
    {
        var roleId = await SeedCustomRole("Some Role");
        var service = CreateService();

        var result = await service.AssignUserRolesAsync(Guid.NewGuid(), new[] { roleId });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("User membership not found");
    }

    [Fact]
    public async Task AssignUserRoles_InvalidRoleId_ShouldFail()
    {
        var (userTenantId, _) = await SeedUserTenantWithRoles();
        var service = CreateService();

        var result = await service.AssignUserRolesAsync(userTenantId, new[] { Guid.NewGuid() });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("One or more role IDs are invalid");
    }

    // ── Tenant Context Not Resolved ──────────────────────────────────

    [Fact]
    public async Task GetRoles_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.GetRolesAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
