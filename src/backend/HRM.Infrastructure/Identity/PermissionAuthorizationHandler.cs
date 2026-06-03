using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Evaluates PermissionRequirement by checking the "permissions" claims in the JWT.
/// NFR-4: Logs authorization failures with user, endpoint, and missing permission.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var permissionClaims = context.User.FindAll("permissions").Select(c => c.Value).ToHashSet();

        if (permissionClaims.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
        else
        {
            // NFR-4: Log authorization failure details
            var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
            var tenantId = context.User.FindFirst("tenant_id")?.Value ?? "unknown";

            _logger.LogWarning(
                "Authorization denied. User={UserId}, Tenant={TenantId}, MissingPermission={Permission}",
                userId, tenantId, requirement.Permission);
        }

        return Task.CompletedTask;
    }
}
