using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Users.Commands;

/// <summary>
/// US-ADM-005 AC-3/FR-4: replace a membership's role set with a new COMPLETE set. Writes assigned_at/
/// assigned_by and a before/after audit. BR-2: a Tenant Admin cannot remove the Tenant Owner role.
/// </summary>
public sealed record EditUserRolesCommand(Guid UserTenantId, IReadOnlyList<Guid> RoleIds)
    : IRequest<Result>;

public sealed class EditUserRolesCommandHandler : IRequestHandler<EditUserRolesCommand, Result>
{
    private readonly IUserManagementService _service;

    public EditUserRolesCommandHandler(IUserManagementService service) => _service = service;

    public Task<Result> Handle(EditUserRolesCommand request, CancellationToken cancellationToken)
        => _service.EditRolesAsync(request.UserTenantId, request.RoleIds, cancellationToken);
}
