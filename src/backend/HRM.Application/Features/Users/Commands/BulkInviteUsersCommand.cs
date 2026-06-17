using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Users.DTOs;
using MediatR;

namespace HRM.Application.Features.Users.Commands;

/// <summary>
/// US-ADM-005 FR-3: bulk/CSV invite. Accepts a parsed array of {email, roleNames}; valid rows create
/// invitations, invalid rows are returned as per-row errors WITHOUT blocking the valid ones. CSV parsing
/// is a thin controller concern — this command takes the already-parsed rows.
/// </summary>
public sealed record BulkInviteUsersCommand(IReadOnlyList<BulkInviteRow> Rows)
    : IRequest<Result<InviteResultDto>>;

public sealed class BulkInviteUsersCommandHandler
    : IRequestHandler<BulkInviteUsersCommand, Result<InviteResultDto>>
{
    private readonly IUserManagementService _service;

    public BulkInviteUsersCommandHandler(IUserManagementService service) => _service = service;

    public Task<Result<InviteResultDto>> Handle(BulkInviteUsersCommand request, CancellationToken cancellationToken)
        => _service.BulkInviteAsync(request.Rows, cancellationToken);
}
