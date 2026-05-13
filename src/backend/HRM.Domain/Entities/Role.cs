namespace HRM.Domain.Entities;

/// <summary>
/// Represents a role within a tenant. Built-in roles are seeded and non-editable.
/// </summary>
public sealed class Role
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; } // null for system-level roles
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<UserTenantRole> UserTenantRoles { get; set; } = new List<UserTenantRole>();
}
