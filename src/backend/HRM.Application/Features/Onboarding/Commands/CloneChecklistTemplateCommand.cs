using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>Clones an existing checklist template incl. all its tasks under a new name (US-ONB-001 FR-6).</summary>
public sealed record CloneChecklistTemplateCommand(Guid TemplateId, string? NewName)
    : IRequest<Result<OnboardingTemplateDto>>;

public sealed class CloneChecklistTemplateCommandHandler
    : IRequestHandler<CloneChecklistTemplateCommand, Result<OnboardingTemplateDto>>
{
    private readonly IOnboardingTemplateService _service;

    public CloneChecklistTemplateCommandHandler(IOnboardingTemplateService service) => _service = service;

    public Task<Result<OnboardingTemplateDto>> Handle(CloneChecklistTemplateCommand request, CancellationToken cancellationToken)
        => _service.CloneAsync(request.TemplateId, request.NewName, cancellationToken);
}
