using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Impersonation.DTOs;
using MediatR;

namespace HRM.Application.Features.Impersonation.Commands;

/// <summary>
/// Ends an active impersonation session (US-ADM-003 AC-3). Callable while impersonating (the tenant-UI banner's
/// "End Session" sends the impersonation token) — the <c>ImpersonationReadOnlyBehavior</c> allowlists it so it is
/// never blocked by the read-only gate. The session id is taken from the route, not the body.
/// </summary>
public sealed record EndImpersonationCommand(Guid SessionId) : IRequest<Result<EndImpersonationResultDto>>;

public sealed class EndImpersonationCommandHandler
    : IRequestHandler<EndImpersonationCommand, Result<EndImpersonationResultDto>>
{
    private readonly IImpersonationService _service;

    public EndImpersonationCommandHandler(IImpersonationService service) => _service = service;

    public Task<Result<EndImpersonationResultDto>> Handle(
        EndImpersonationCommand request, CancellationToken cancellationToken)
        => _service.EndAsync(request.SessionId, cancellationToken);
}
