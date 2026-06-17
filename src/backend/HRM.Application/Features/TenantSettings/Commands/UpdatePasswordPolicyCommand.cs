using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Commands;

/// <summary>US-ADM-006 AC-4/FR-5: update the current tenant's password policy.</summary>
public sealed record UpdatePasswordPolicyCommand(UpdatePasswordPolicyRequest Request) : IRequest<Result<PasswordPolicyDto>>;

public sealed class UpdatePasswordPolicyCommandHandler
    : IRequestHandler<UpdatePasswordPolicyCommand, Result<PasswordPolicyDto>>
{
    private readonly ITenantSettingsService _service;

    public UpdatePasswordPolicyCommandHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<PasswordPolicyDto>> Handle(UpdatePasswordPolicyCommand request, CancellationToken cancellationToken)
        => _service.UpdatePasswordPolicyAsync(request.Request, cancellationToken);
}
