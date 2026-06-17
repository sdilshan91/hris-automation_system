using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Commands;

/// <summary>US-ADM-006 FR-3: update the current tenant's primary brand color (hex).</summary>
public sealed record UpdatePrimaryColorCommand(string PrimaryColor) : IRequest<Result<BrandingDto>>;

public sealed class UpdatePrimaryColorCommandHandler
    : IRequestHandler<UpdatePrimaryColorCommand, Result<BrandingDto>>
{
    private readonly ITenantSettingsService _service;

    public UpdatePrimaryColorCommandHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<BrandingDto>> Handle(UpdatePrimaryColorCommand request, CancellationToken cancellationToken)
        => _service.UpdatePrimaryColorAsync(request.PrimaryColor, cancellationToken);
}
