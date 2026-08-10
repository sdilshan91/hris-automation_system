using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>
/// GAP-013 / US-ONB-002 AC-3: the employee's current ACTIVE onboarding checklist, or null when none exists.
/// Backs the replace/merge prompt the assignment screen shows before overwriting an existing checklist.
/// </summary>
public sealed record GetActiveChecklistByEmployeeQuery(Guid EmployeeId)
    : IRequest<Result<OnboardingChecklistInstanceDto?>>;

public sealed class GetActiveChecklistByEmployeeQueryHandler
    : IRequestHandler<GetActiveChecklistByEmployeeQuery, Result<OnboardingChecklistInstanceDto?>>
{
    private readonly IOnboardingChecklistService _service;

    public GetActiveChecklistByEmployeeQueryHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<OnboardingChecklistInstanceDto?>> Handle(
        GetActiveChecklistByEmployeeQuery request, CancellationToken cancellationToken)
        => _service.GetActiveByEmployeeAsync(request.EmployeeId, cancellationToken);
}
