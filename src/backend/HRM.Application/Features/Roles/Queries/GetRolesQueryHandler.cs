using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using MediatR;

namespace HRM.Application.Features.Roles.Queries;

public sealed class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, Result<IReadOnlyList<RoleDto>>>
{
    private readonly IRoleService _roleService;

    public GetRolesQueryHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public Task<Result<IReadOnlyList<RoleDto>>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        return _roleService.GetRolesAsync(cancellationToken);
    }
}
