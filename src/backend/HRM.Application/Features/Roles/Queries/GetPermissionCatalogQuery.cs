using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using MediatR;

namespace HRM.Application.Features.Roles.Queries;

/// <summary>
/// Returns the full permission catalog (all valid permissions grouped by module).
/// </summary>
public sealed record GetPermissionCatalogQuery : IRequest<Result<PermissionCatalogDto>>;
