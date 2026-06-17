using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>
/// Lists active onboarding templates applicable to an employee (US-ONB-002 AC-1/FR-1): filtered by the
/// employee's department + job title, plus universal templates. Active only (BR-1).
/// </summary>
public sealed record GetApplicableTemplatesQuery(Guid EmployeeId)
    : IRequest<Result<IReadOnlyList<ApplicableTemplateDto>>>;

public sealed class GetApplicableTemplatesQueryHandler
    : IRequestHandler<GetApplicableTemplatesQuery, Result<IReadOnlyList<ApplicableTemplateDto>>>
{
    private readonly IOnboardingChecklistService _service;

    public GetApplicableTemplatesQueryHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<IReadOnlyList<ApplicableTemplateDto>>> Handle(
        GetApplicableTemplatesQuery request, CancellationToken cancellationToken)
        => _service.GetApplicableTemplatesAsync(request.EmployeeId, cancellationToken);
}
