// ============================================================================
// US-AUTH-006: RBAC Unit Tests - Permission Catalog
// Follow-up: Integration tests with Testcontainers (database-backed) are NOT
// included in this pass. Add them in a future sprint when the test infra matures.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;

namespace HRM.Tests.Unit;

/// <summary>
/// Tests the static PermissionCatalog source of truth.
/// </summary>
public sealed class PermissionCatalogTests
{
    [Fact]
    public void AllPermissions_ShouldContainAtLeast40Entries()
    {
        PermissionCatalog.AllPermissions.Should().HaveCountGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void AllPermissions_ShouldFollowModuleActionPattern()
    {
        foreach (var perm in PermissionCatalog.AllPermissions)
        {
            var parts = perm.Split('.');
            parts.Length.Should().BeGreaterThanOrEqualTo(2, $"Permission '{perm}' must have at least Module.Action");
            parts[0].Should().NotBeNullOrWhiteSpace($"Module segment of '{perm}' should not be empty");
            parts[1].Should().NotBeNullOrWhiteSpace($"Action segment of '{perm}' should not be empty");
        }
    }

    [Fact]
    public void AllPermissions_ShouldContainNoDuplicates()
    {
        PermissionCatalog.AllPermissions.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("Payroll.View", true)]
    [InlineData("Leave.Approve.Team", true)]
    [InlineData("Employee.View.All", true)]
    [InlineData("NonExistent.Permission", false)]
    [InlineData("", false)]
    [InlineData("Payroll", false)]
    public void IsValid_ShouldReturnCorrectResult(string permission, bool expected)
    {
        PermissionCatalog.IsValid(permission).Should().Be(expected);
    }

    [Fact]
    public void ByModule_ShouldGroupAllPermissions()
    {
        var totalInGroups = PermissionCatalog.ByModule.Values.SelectMany(v => v).Count();
        totalInGroups.Should().Be(PermissionCatalog.AllPermissions.Count);
    }

    [Fact]
    public void ByModule_ShouldContainExpectedModules()
    {
        PermissionCatalog.ByModule.Keys.Should().Contain("Employee");
        PermissionCatalog.ByModule.Keys.Should().Contain("Leave");
        PermissionCatalog.ByModule.Keys.Should().Contain("Payroll");
        PermissionCatalog.ByModule.Keys.Should().Contain("Roles");
    }

    [Fact]
    public void BuiltInRoles_ShouldContainExpectedRoles()
    {
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Tenant Owner");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Tenant Admin");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("HR Officer");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Manager");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Employee");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Recruiter");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Auditor");
    }

    [Fact]
    public void DefaultPermissionsFor_TenantOwner_ShouldReturnAllPermissions()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.TenantOwner);
        perms.Should().BeEquivalentTo(PermissionCatalog.AllPermissions);
    }

    [Fact]
    public void DefaultPermissionsFor_Employee_ShouldReturnSubset()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Employee);
        perms.Should().NotBeEmpty();
        perms.Should().Contain("Leave.Apply");
        perms.Should().Contain("Employee.View.Own");
        perms.Should().NotContain("Roles.Manage");
        perms.Should().NotContain("Payroll.Run");
    }

    [Fact]
    public void DefaultPermissionsFor_UnknownRole_ShouldReturnEmpty()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor("NonExistentRole");
        perms.Should().BeEmpty();
    }

    [Fact]
    public void SystemRoles_ShouldContainExpectedRoles()
    {
        PermissionCatalog.SystemRoles.All.Should().Contain("System Super Admin");
        PermissionCatalog.SystemRoles.All.Should().Contain("System Support");
        PermissionCatalog.SystemRoles.All.Should().Contain("System Billing");
        PermissionCatalog.SystemRoles.All.Should().Contain("System Compliance");
    }

    [Fact]
    public void DefaultPermissionsFor_AllBuiltInRoles_ShouldContainOnlyValidPermissions()
    {
        foreach (var roleName in PermissionCatalog.BuiltInRoles.All)
        {
            var perms = PermissionCatalog.DefaultPermissionsFor(roleName);
            foreach (var perm in perms)
            {
                PermissionCatalog.IsValid(perm).Should().BeTrue(
                    $"Permission '{perm}' for role '{roleName}' should be in the catalog");
            }
        }
    }
}
