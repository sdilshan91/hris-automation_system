using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Roles.Commands;

public sealed class AssignUserRolesCommandHandler : IRequestHandler<AssignUserRolesCommand, Result>
{
    private readonly IRoleService _roleService;

    public AssignUserRolesCommandHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public Task<Result> Handle(AssignUserRolesCommand request, CancellationToken cancellationToken)
    {
        return _roleService.AssignUserRolesAsync(request.UserTenantId, request.RoleIds, cancellationToken);
    }
}
