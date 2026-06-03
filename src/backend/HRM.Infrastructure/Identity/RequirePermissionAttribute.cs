using Microsoft.AspNetCore.Authorization;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Convenience attribute for permission-based authorization on controllers/actions.
/// Usage: [RequirePermission("Payroll.View")]
/// This maps to an authorization policy "Permission:Payroll.View" which is
/// dynamically created by PermissionPolicyProvider.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission)
        : base(policy: $"{PermissionPolicyProvider.PolicyPrefix}{permission}")
    {
    }
}
