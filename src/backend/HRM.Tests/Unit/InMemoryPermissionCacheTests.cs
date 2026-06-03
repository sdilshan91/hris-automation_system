// ============================================================================
// US-AUTH-006: RBAC Unit Tests - InMemoryPermissionCache
// Validates cache get/set/invalidation behavior.
// ============================================================================

using FluentAssertions;
using HRM.Infrastructure.Caching;

namespace HRM.Tests.Unit;

public sealed class InMemoryPermissionCacheTests
{
    private readonly InMemoryPermissionCache _cache = new();

    [Fact]
    public async Task GetPermissions_CacheMiss_ShouldReturnNull()
    {
        var result = await _cache.GetPermissionsAsync(Guid.NewGuid(), Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetThenGet_ShouldReturnCachedPermissions()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permissions = new List<string> { "Leave.Apply", "Attendance.CheckIn" };

        await _cache.SetPermissionsAsync(tenantId, userId, permissions);

        var result = await _cache.GetPermissionsAsync(tenantId, userId);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(permissions);
    }

    [Fact]
    public async Task Invalidate_ShouldRemoveCachedEntry()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _cache.SetPermissionsAsync(tenantId, userId, new[] { "Leave.Apply" });
        await _cache.InvalidateAsync(tenantId, userId);

        var result = await _cache.GetPermissionsAsync(tenantId, userId);
        result.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateTenant_ShouldRemoveAllUsersInTenant()
    {
        var tenantId = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var user3 = Guid.NewGuid();

        await _cache.SetPermissionsAsync(tenantId, user1, new[] { "Leave.Apply" });
        await _cache.SetPermissionsAsync(tenantId, user2, new[] { "Payroll.View" });
        await _cache.SetPermissionsAsync(otherTenantId, user3, new[] { "Reports.View" });

        await _cache.InvalidateTenantAsync(tenantId);

        (await _cache.GetPermissionsAsync(tenantId, user1)).Should().BeNull();
        (await _cache.GetPermissionsAsync(tenantId, user2)).Should().BeNull();
        // Other tenant should NOT be affected
        (await _cache.GetPermissionsAsync(otherTenantId, user3)).Should().NotBeNull();
    }

    [Fact]
    public async Task SetPermissions_OverwriteExisting_ShouldUpdateCache()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _cache.SetPermissionsAsync(tenantId, userId, new[] { "Leave.Apply" });
        await _cache.SetPermissionsAsync(tenantId, userId, new[] { "Leave.Apply", "Payroll.View" });

        var result = await _cache.GetPermissionsAsync(tenantId, userId);
        result.Should().HaveCount(2);
        result.Should().Contain("Payroll.View");
    }
}
