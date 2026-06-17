using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Users.Commands;

/// <summary>
/// US-ADM-005 AC-4: deactivate a user's membership in THIS tenant — sets status=Disabled, revokes the
/// user's refresh tokens in this tenant only, audits. BR-3: cannot deactivate one's own membership.
/// </summary>
public sealed record DeactivateUserCommand(Guid UserTenantId) : IRequest<Result>;

public sealed class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand, Result>
{
    private readonly IUserManagementService _service;

    public DeactivateUserCommandHandler(IUserManagementService service) => _service = service;

    public Task<Result> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
        => _service.DeactivateAsync(request.UserTenantId, cancellationToken);
}
