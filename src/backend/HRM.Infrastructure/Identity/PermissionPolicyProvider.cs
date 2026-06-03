using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Custom authorization policy provider that dynamically creates policies
/// for permission-based authorization. Any policy name starting with "Permission:"
/// is handled by creating a policy with a PermissionRequirement.
///
/// This allows controllers to use [Authorize(Policy = "Permission:Payroll.View")]
/// or the convenience attribute [RequirePermission("Payroll.View")].
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "Permission:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var permission = policyName[PolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallback.GetFallbackPolicyAsync();
}
