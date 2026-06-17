using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Users.DTOs;
using MediatR;

namespace HRM.Application.Features.Users.Commands;

/// <summary>US-ADM-005 BR-6: resend an invitation — generates a NEW one-time token + fresh 72h expiry.</summary>
public sealed record ResendInvitationCommand(Guid InvitationId) : IRequest<Result<InvitationDto>>;

public sealed class ResendInvitationCommandHandler
    : IRequestHandler<ResendInvitationCommand, Result<InvitationDto>>
{
    private readonly IUserManagementService _service;

    public ResendInvitationCommandHandler(IUserManagementService service) => _service = service;

    public Task<Result<InvitationDto>> Handle(ResendInvitationCommand request, CancellationToken cancellationToken)
        => _service.ResendInvitationAsync(request.InvitationId, cancellationToken);
}
