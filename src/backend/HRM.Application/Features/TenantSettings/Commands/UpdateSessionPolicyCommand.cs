using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Commands;

/// <summary>US-ADM-006 FR-6: update the current tenant's session policy.</summary>
public sealed record UpdateSessionPolicyCommand(UpdateSessionPolicyRequest Request) : IRequest<Result<SessionPolicyDto>>;

public sealed class UpdateSessionPolicyCommandHandler
    : IRequestHandler<UpdateSessionPolicyCommand, Result<SessionPolicyDto>>
{
    private readonly ITenantSettingsService _service;

    public UpdateSessionPolicyCommandHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<SessionPolicyDto>> Handle(UpdateSessionPolicyCommand request, CancellationToken cancellationToken)
        => _service.UpdateSessionPolicyAsync(request.Request, cancellationToken);
}
