using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Commands;

/// <summary>US-REC-010 FR-5/BR-7 (ISSUE-140): update the current tenant's auto-create-user-on-hire toggle.</summary>
public sealed record UpdateHiringSettingsCommand(UpdateHiringSettingsRequest Request) : IRequest<Result<HiringSettingsDto>>;

public sealed class UpdateHiringSettingsCommandHandler
    : IRequestHandler<UpdateHiringSettingsCommand, Result<HiringSettingsDto>>
{
    private readonly ITenantSettingsService _service;

    public UpdateHiringSettingsCommandHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<HiringSettingsDto>> Handle(UpdateHiringSettingsCommand request, CancellationToken cancellationToken)
        => _service.UpdateHiringSettingsAsync(request.Request, cancellationToken);
}
