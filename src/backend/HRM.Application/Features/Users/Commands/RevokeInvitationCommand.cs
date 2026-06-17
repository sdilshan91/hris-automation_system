using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Users.Commands;

/// <summary>US-ADM-005 AC-2: revoke a pending invitation (status → Revoked).</summary>
public sealed record RevokeInvitationCommand(Guid InvitationId) : IRequest<Result>;

public sealed class RevokeInvitationCommandHandler : IRequestHandler<RevokeInvitationCommand, Result>
{
    private readonly IUserManagementService _service;

    public RevokeInvitationCommandHandler(IUserManagementService service) => _service = service;

    public Task<Result> Handle(RevokeInvitationCommand request, CancellationToken cancellationToken)
        => _service.RevokeInvitationAsync(request.InvitationId, cancellationToken);
}
