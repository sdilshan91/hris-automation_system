using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using MediatR;

namespace HRM.Application.Features.Roles.Commands;

/// <summary>
/// Creates a new custom role in the current tenant.
/// </summary>
public sealed record CreateRoleCommand(
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions
) : IRequest<Result<RoleDto>>;
