using Microsoft.AspNetCore.Authorization;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Resource-based authorization requirement that validates a manager can only
/// act on their direct reports. Used for team-scoped permissions like
/// Leave.Approve.Team where the manager must be the employee's manager.
///
/// AC-5: Resource-level authorization validates the manager-report relationship.
/// FR-5 Layer 3: Resource-based authorization handler.
/// </summary>
public sealed class TeamScopeRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// The team-scoped permission the caller claims to have (e.g. "Leave.Approve.Team").
    /// </summary>
    public string Permission { get; }

    public TeamScopeRequirement(string permission)
    {
        Permission = permission;
    }
}

/// <summary>
/// Resource context passed to the TeamScopeAuthorizationHandler.
/// Contains the target employee's information needed for scope validation.
/// </summary>
public sealed class TeamScopeResource
{
    /// <summary>
    /// The employee whose record is being acted upon.
    /// </summary>
    public Guid TargetEmployeeUserId { get; init; }

    /// <summary>
    /// The manager (UserId) of the target employee. Must be loaded from
    /// the employee/org-chart before authorization is checked.
    /// </summary>
    public Guid? ManagerUserId { get; init; }
}
