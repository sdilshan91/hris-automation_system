using Microsoft.AspNetCore.Authorization;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Convenience attribute for permission-based authorization on controllers/actions.
/// Usage: [RequirePermission("Payroll.View")] — requires that single permission.
/// Usage: [RequirePermission("Performance.SetGoal.Team", "Performance.SetGoal.All")] — OR semantics:
/// the caller satisfies the policy if they hold ANY of the listed permissions.
/// This maps to an authorization policy "Permission:&lt;comma-joined permissions&gt;" which is
/// dynamically created by PermissionPolicyProvider.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(params string[] permissions)
        : base(policy: $"{PermissionPolicyProvider.PolicyPrefix}{string.Join(',', permissions)}")
    {
    }
}
