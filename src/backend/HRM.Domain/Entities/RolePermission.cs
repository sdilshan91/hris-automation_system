namespace HRM.Domain.Entities;

/// <summary>
/// Maps permissions to roles. Composite key of RoleId + Permission.
/// Permissions follow the pattern: Module.Action[.Scope]
/// </summary>
public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = string.Empty;

    // Navigation
    public Role Role { get; set; } = null!;
}
