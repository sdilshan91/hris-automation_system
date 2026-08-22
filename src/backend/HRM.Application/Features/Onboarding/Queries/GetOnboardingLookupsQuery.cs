using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>B6: scope/responsibility lookups for the onboarding template builder (US-ONB-001).</summary>
public sealed record GetOnboardingLookupsQuery : IRequest<Result<OnboardingLookupsDto>>;

public sealed class GetOnboardingLookupsQueryHandler
    : IRequestHandler<GetOnboardingLookupsQuery, Result<OnboardingLookupsDto>>
{
    private readonly IOnboardingTemplateService _service;

    public GetOnboardingLookupsQueryHandler(IOnboardingTemplateService service) => _service = service;

    public Task<Result<OnboardingLookupsDto>> Handle(
        GetOnboardingLookupsQuery request, CancellationToken cancellationToken)
        => _service.GetLookupsAsync(cancellationToken);
}
