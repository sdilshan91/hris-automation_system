using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using MediatR;

namespace HRM.Application.Features.Roles.Commands;

/// <summary>
/// Updates an existing custom role's name, description, and permissions.
/// Built-in roles cannot be updated.
/// </summary>
public sealed record UpdateRoleCommand(
    Guid RoleId,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions
) : IRequest<Result<RoleDto>>;
