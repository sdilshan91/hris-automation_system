using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.DTOs;
using MediatR;

namespace HRM.Application.Features.Roles.Commands;

public sealed class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, Result<RoleDto>>
{
    private readonly IRoleService _roleService;

    public UpdateRoleCommandHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public Task<Result<RoleDto>> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        return _roleService.UpdateRoleAsync(request.RoleId, request.Name, request.Description, request.Permissions, cancellationToken);
    }
}
