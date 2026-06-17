using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Users.DTOs;
using MediatR;

namespace HRM.Application.Features.Users.Queries;

/// <summary>US-ADM-005 AC-2: the Pending Invitations tab.</summary>
public sealed record ListInvitationsQuery : IRequest<Result<IReadOnlyList<InvitationDto>>>;

public sealed class ListInvitationsQueryHandler
    : IRequestHandler<ListInvitationsQuery, Result<IReadOnlyList<InvitationDto>>>
{
    private readonly IUserManagementService _service;

    public ListInvitationsQueryHandler(IUserManagementService service) => _service = service;

    public Task<Result<IReadOnlyList<InvitationDto>>> Handle(ListInvitationsQuery request, CancellationToken cancellationToken)
        => _service.ListInvitationsAsync(cancellationToken);
}
