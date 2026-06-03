using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using MediatR;

namespace HRM.Application.Features.Roles.Queries;

/// <summary>
/// Lists all roles for the current tenant (built-in + custom).
/// </summary>
public sealed record GetRolesQuery : IRequest<Result<IReadOnlyList<RoleDto>>>;
