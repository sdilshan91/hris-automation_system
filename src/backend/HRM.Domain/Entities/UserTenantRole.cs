namespace HRM.Domain.Entities;

/// <summary>
/// Many-to-many between UserTenant and Role.
/// </summary>
public sealed class UserTenantRole
{
    public Guid UserTenantId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? AssignedBy { get; set; }

    // Navigation
    public UserTenant UserTenant { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
