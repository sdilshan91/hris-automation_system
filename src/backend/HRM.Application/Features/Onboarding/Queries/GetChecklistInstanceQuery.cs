using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>Gets a single assigned onboarding checklist instance with its tasks (US-ONB-002), tenant-scoped.</summary>
public sealed record GetChecklistInstanceQuery(Guid ChecklistInstanceId)
    : IRequest<Result<OnboardingChecklistInstanceDto>>;

public sealed class GetChecklistInstanceQueryHandler
    : IRequestHandler<GetChecklistInstanceQuery, Result<OnboardingChecklistInstanceDto>>
{
    private readonly IOnboardingChecklistService _service;

    public GetChecklistInstanceQueryHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<OnboardingChecklistInstanceDto>> Handle(
        GetChecklistInstanceQuery request, CancellationToken cancellationToken)
        => _service.GetInstanceAsync(request.ChecklistInstanceId, cancellationToken);
}
