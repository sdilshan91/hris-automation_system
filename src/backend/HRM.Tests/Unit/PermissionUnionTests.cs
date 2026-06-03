// ============================================================================
// US-AUTH-006: RBAC Unit Tests - Permission Union Logic (BR-4)
// Verifies that a user with multiple roles gets the union of all permissions.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;

namespace HRM.Tests.Unit;

/// <summary>
/// Tests the permission union logic: when a user has multiple roles,
/// their effective permissions are the set union of all assigned role permissions.
/// </summary>
public sealed class PermissionUnionTests
{
    [Fact]
    public void UnionOfTwoRoles_WithOverlap_ShouldReturnDistinctPermissions()
    {
        // Arrange: Manager and Recruiter share Employee.ViewAll
        var managerPerms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Manager);
        var recruiterPerms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Recruiter);

        // Act: compute union (as AuthService does)
        var union = managerPerms
            .Concat(recruiterPerms)
            .Distinct()
            .ToList();

        // Assert
        union.Should().OnlyHaveUniqueItems("effective permissions should be deduplicated");
        union.Count.Should().BeLessThanOrEqualTo(managerPerms.Count + recruiterPerms.Count);

        // Union should contain perms from both
        union.Should().Contain("Leave.Approve.Team", "from Manager role");
        union.Should().Contain("Recruitment.Manage", "from Recruiter role");
    }

    [Fact]
    public void UnionOfSingleRole_ShouldEqualRolePermissions()
    {
        var employeePerms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Employee);

        var union = employeePerms.Distinct().ToList();

        union.Should().BeEquivalentTo(employeePerms);
    }

    [Fact]
    public void UnionOfEmployeeAndManager_ShouldContainBothSets()
    {
        var employeePerms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Employee);
        var managerPerms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Manager);

        var union = employeePerms
            .Concat(managerPerms)
            .Distinct()
            .ToList();

        // Employee perms
        union.Should().Contain("Leave.Apply");
        union.Should().Contain("Employee.View.Own");
        union.Should().Contain("Attendance.CheckIn");

        // Manager perms
        union.Should().Contain("Leave.Approve.Team");
        union.Should().Contain("Employee.View.Team");

        // The union should be larger than either individual set
        union.Count.Should().BeGreaterThan(employeePerms.Count);
        union.Count.Should().BeGreaterThan(managerPerms.Count);
    }

    [Fact]
    public void UnionWithTenantOwner_ShouldEqualAllPermissions()
    {
        // Tenant Owner has all permissions; union with any other role should still be all
        var ownerPerms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.TenantOwner);
        var employeePerms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Employee);

        var union = ownerPerms
            .Concat(employeePerms)
            .Distinct()
            .ToList();

        union.Should().BeEquivalentTo(PermissionCatalog.AllPermissions);
    }

    [Fact]
    public void EmptyRoles_ShouldYieldEmptyPermissions()
    {
        var noPermissions = Array.Empty<string>();
        var union = noPermissions.Distinct().ToList();
        union.Should().BeEmpty();
    }
}
