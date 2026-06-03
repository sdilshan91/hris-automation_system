using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Resource-based authorization handler that validates team scope.
/// A user with a .Team scoped permission (e.g. Leave.Approve.Team) can only
/// act on records belonging to their direct reports.
///
/// Usage (in a controller action):
///   var resource = new TeamScopeResource { TargetEmployeeUserId = empId, ManagerUserId = loadedManagerId };
///   var result = await _authorizationService.AuthorizeAsync(User, resource, new TeamScopeRequirement("Leave.Approve.Team"));
///   if (!result.Succeeded) return Forbid();
///
/// AC-5: Manager attempting to approve leave for non-report gets 403.
/// FR-5 Layer 3: Resource-based authorization.
/// </summary>
public sealed class TeamScopeAuthorizationHandler : AuthorizationHandler<TeamScopeRequirement, TeamScopeResource>
{
    private readonly ILogger<TeamScopeAuthorizationHandler> _logger;

    public TeamScopeAuthorizationHandler(ILogger<TeamScopeAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TeamScopeRequirement requirement,
        TeamScopeResource resource)
    {
        // Check that the caller has the required permission claim
        var permissionClaims = context.User.FindAll("permissions").Select(c => c.Value).ToHashSet();

        if (!permissionClaims.Contains(requirement.Permission))
        {
            LogDenied(context, requirement, resource, "Missing permission claim");
            return Task.CompletedTask;
        }

        // Check if the caller also has the .All variant (bypasses team check)
        var allPermission = requirement.Permission.Replace(".Team", ".All");
        if (permissionClaims.Contains(allPermission))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Validate manager-report relationship
        var callerUserId = context.User.FindFirst("sub")?.Value;
        if (callerUserId is null)
        {
            LogDenied(context, requirement, resource, "No sub claim");
            return Task.CompletedTask;
        }

        if (resource.ManagerUserId.HasValue &&
            Guid.TryParse(callerUserId, out var callerId) &&
            callerId == resource.ManagerUserId.Value)
        {
            context.Succeed(requirement);
        }
        else
        {
            LogDenied(context, requirement, resource, "Not the target employee's manager");
        }

        return Task.CompletedTask;
    }

    private void LogDenied(
        AuthorizationHandlerContext context,
        TeamScopeRequirement requirement,
        TeamScopeResource resource,
        string reason)
    {
        var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
        var tenantId = context.User.FindFirst("tenant_id")?.Value ?? "unknown";

        _logger.LogWarning(
            "Team-scope authorization denied. User={UserId}, Tenant={TenantId}, Permission={Permission}, TargetEmployee={TargetEmployee}, Reason={Reason}",
            userId, tenantId, requirement.Permission, resource.TargetEmployeeUserId, reason);
    }
}
