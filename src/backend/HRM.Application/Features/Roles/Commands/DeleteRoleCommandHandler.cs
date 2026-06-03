using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Roles.Commands;

public sealed class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand, Result>
{
    private readonly IRoleService _roleService;

    public DeleteRoleCommandHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public Task<Result> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        return _roleService.DeleteRoleAsync(request.RoleId, cancellationToken);
    }
}
