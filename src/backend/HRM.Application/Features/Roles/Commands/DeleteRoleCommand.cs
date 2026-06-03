using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Roles.Commands;

/// <summary>
/// Deletes a custom role from the current tenant.
/// Built-in roles cannot be deleted.
/// When a deleted role is assigned to users, those assignments are removed.
/// </summary>
public sealed record DeleteRoleCommand(Guid RoleId) : IRequest<Result>;
