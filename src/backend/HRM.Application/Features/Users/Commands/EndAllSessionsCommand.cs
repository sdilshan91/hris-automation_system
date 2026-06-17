using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Users.Commands;

/// <summary>US-ADM-005 FR-5: revoke all of the user's refresh tokens within the current tenant only, audit.</summary>
public sealed record EndAllSessionsCommand(Guid UserTenantId) : IRequest<Result>;

public sealed class EndAllSessionsCommandHandler : IRequestHandler<EndAllSessionsCommand, Result>
{
    private readonly IUserManagementService _service;

    public EndAllSessionsCommandHandler(IUserManagementService service) => _service = service;

    public Task<Result> Handle(EndAllSessionsCommand request, CancellationToken cancellationToken)
        => _service.EndAllSessionsAsync(request.UserTenantId, cancellationToken);
}
