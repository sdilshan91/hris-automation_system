using Microsoft.AspNetCore.Authorization;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Authorization requirement that demands the caller has at least one of the listed
/// permissions in their JWT claims (OR semantics). Used with the
/// [RequirePermission("Payroll.View")] / [RequirePermission("A", "B")] attribute.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; }

    public PermissionRequirement(params string[] permissions)
    {
        Permissions = permissions;
    }
}
