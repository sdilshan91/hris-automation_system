using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Roles.Commands;

/// <summary>
/// Assigns a set of roles to a user's tenant membership.
/// Replaces the user's current roles with the provided set.
/// Protects the Tenant Owner role: cannot remove it from the sole owner.
/// </summary>
public sealed record AssignUserRolesCommand(
    Guid UserTenantId,
    IReadOnlyList<Guid> RoleIds
) : IRequest<Result>;
