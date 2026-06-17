using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Users.DTOs;
using MediatR;

namespace HRM.Application.Features.Users.Commands;

/// <summary>US-ADM-005 AC-2/FR-2: invite a single user by email with a role set.</summary>
public sealed record InviteUserCommand(string Email, IReadOnlyList<Guid> RoleIds)
    : IRequest<Result<InviteResultDto>>;

public sealed class InviteUserCommandHandler : IRequestHandler<InviteUserCommand, Result<InviteResultDto>>
{
    private readonly IUserManagementService _service;

    public InviteUserCommandHandler(IUserManagementService service) => _service = service;

    public Task<Result<InviteResultDto>> Handle(InviteUserCommand request, CancellationToken cancellationToken)
        => _service.InviteAsync(request.Email, request.RoleIds, cancellationToken);
}
