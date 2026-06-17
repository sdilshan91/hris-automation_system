using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>Gets a single onboarding checklist template (with its tasks) by id, tenant-scoped (US-ONB-001).</summary>
public sealed record GetTemplateByIdQuery(Guid TemplateId) : IRequest<Result<OnboardingTemplateDto>>;

public sealed class GetTemplateByIdQueryHandler
    : IRequestHandler<GetTemplateByIdQuery, Result<OnboardingTemplateDto>>
{
    private readonly IOnboardingTemplateService _service;

    public GetTemplateByIdQueryHandler(IOnboardingTemplateService service) => _service = service;

    public Task<Result<OnboardingTemplateDto>> Handle(GetTemplateByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.TemplateId, cancellationToken);
}
