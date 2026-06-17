using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Commands;

/// <summary>US-ADM-006 AC-1: update the current tenant's organization profile.</summary>
public sealed record UpdateOrgProfileCommand(UpdateOrgProfileRequest Request) : IRequest<Result<OrgProfileDto>>;

public sealed class UpdateOrgProfileCommandHandler
    : IRequestHandler<UpdateOrgProfileCommand, Result<OrgProfileDto>>
{
    private readonly ITenantSettingsService _service;

    public UpdateOrgProfileCommandHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<OrgProfileDto>> Handle(UpdateOrgProfileCommand request, CancellationToken cancellationToken)
        => _service.UpdateOrgProfileAsync(request.Request, cancellationToken);
}
