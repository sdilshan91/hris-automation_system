namespace HRM.Domain.Entities;

/// <summary>
/// Represents a user's membership in a tenant.
/// Each user can belong to multiple tenants with different roles.
/// </summary>
public sealed class UserTenant
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public UserTenantStatus Status { get; set; } = UserTenantStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public ICollection<UserTenantRole> UserTenantRoles { get; set; } = new List<UserTenantRole>();
}

public enum UserTenantStatus
{
    Active = 0,
    Disabled = 1,
    Suspended = 2
}
