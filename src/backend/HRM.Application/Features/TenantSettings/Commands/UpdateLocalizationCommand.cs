using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Commands;

/// <summary>US-ADM-006 AC-3: update the current tenant's localization defaults.</summary>
public sealed record UpdateLocalizationCommand(UpdateLocalizationRequest Request) : IRequest<Result<LocalizationDto>>;

public sealed class UpdateLocalizationCommandHandler
    : IRequestHandler<UpdateLocalizationCommand, Result<LocalizationDto>>
{
    private readonly ITenantSettingsService _service;

    public UpdateLocalizationCommandHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<LocalizationDto>> Handle(UpdateLocalizationCommand request, CancellationToken cancellationToken)
        => _service.UpdateLocalizationAsync(request.Request, cancellationToken);
}
