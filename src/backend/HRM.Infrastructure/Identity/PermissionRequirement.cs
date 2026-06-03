using Microsoft.AspNetCore.Authorization;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Authorization requirement that demands the caller has a specific permission
/// in their JWT claims. Used with [RequirePermission("Payroll.View")] attribute.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
