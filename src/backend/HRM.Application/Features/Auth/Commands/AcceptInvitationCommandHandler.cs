using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

public sealed class AcceptInvitationCommandHandler : IRequestHandler<AcceptInvitationCommand, Result>
{
    private readonly IAuthService _authService;

    public AcceptInvitationCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        return await _authService.AcceptInvitationAsync(
            request.Token,
            request.NewPassword,
            request.IpAddress,
            request.UserAgent,
            cancellationToken);
    }
}
