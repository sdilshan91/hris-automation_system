using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using HRM.Domain.Authorization;
using MediatR;

namespace HRM.Application.Features.Roles.Queries;

/// <summary>
/// Returns the permission catalog from the domain source of truth.
/// No database access needed; the catalog is defined in code.
/// </summary>
public sealed class GetPermissionCatalogQueryHandler : IRequestHandler<GetPermissionCatalogQuery, Result<PermissionCatalogDto>>
{
    public Task<Result<PermissionCatalogDto>> Handle(GetPermissionCatalogQuery request, CancellationToken cancellationToken)
    {
        var dto = new PermissionCatalogDto
        {
            AllPermissions = PermissionCatalog.AllPermissions,
            ByModule = PermissionCatalog.ByModule,
        };

        return Task.FromResult(Result<PermissionCatalogDto>.Success(dto));
    }
}
