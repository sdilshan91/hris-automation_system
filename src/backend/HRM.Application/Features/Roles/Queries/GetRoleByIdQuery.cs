using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using MediatR;

namespace HRM.Application.Features.Roles.Queries;

/// <summary>
/// Gets a single role by ID within the current tenant.
/// </summary>
public sealed record GetRoleByIdQuery(Guid RoleId) : IRequest<Result<RoleDto>>;
