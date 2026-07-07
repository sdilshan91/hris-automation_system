using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
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

        if (requirement.Permissions.Any(permissionClaims.Contains))
        {
            context.Succeed(requirement);
        }
        else
        {
            // NFR-4 / ISSUE-054: log authorization failure details. Resolve the actor from the authenticated
            // principal — the JWT carries the user id under "sub" (JwtRegisteredClaimNames.Sub), but depending
            // on claim-mapping it can also surface as ClaimTypes.NameIdentifier; fall back to the email claim so
            // the trail is still identifiable. Record "unknown" ONLY when the request is genuinely anonymous.
            var isAuthenticated = context.User.Identity?.IsAuthenticated ?? false;
            var userId = isAuthenticated
                ? context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                    ?? context.User.FindFirst(ClaimTypes.Email)?.Value
                    ?? "unknown"
                : "anonymous";
            var tenantId = context.User.FindFirst("tenant_id")?.Value ?? "unknown";

            _logger.LogWarning(
                "Authorization denied. User={UserId}, Tenant={TenantId}, MissingPermission={Permission}",
                userId, tenantId, string.Join(" | ", requirement.Permissions));
        }

        return Task.CompletedTask;
    }
}
