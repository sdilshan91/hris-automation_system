using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>Activates or deactivates a checklist template (US-ONB-001 FR-7, BR-4). No deletion.</summary>
public sealed record SetTemplateActiveCommand(Guid TemplateId, bool IsActive)
    : IRequest<Result<OnboardingTemplateDto>>;

public sealed class SetTemplateActiveCommandHandler
    : IRequestHandler<SetTemplateActiveCommand, Result<OnboardingTemplateDto>>
{
    private readonly IOnboardingTemplateService _service;

    public SetTemplateActiveCommandHandler(IOnboardingTemplateService service) => _service = service;

    public Task<Result<OnboardingTemplateDto>> Handle(SetTemplateActiveCommand request, CancellationToken cancellationToken)
        => request.IsActive
            ? _service.ActivateAsync(request.TemplateId, cancellationToken)
            : _service.DeactivateAsync(request.TemplateId, cancellationToken);
}
