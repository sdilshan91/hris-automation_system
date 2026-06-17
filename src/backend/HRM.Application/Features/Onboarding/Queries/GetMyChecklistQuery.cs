using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>
/// US-ONB-003 AC-1/AC-2/FR-1: returns the logged-in employee's active onboarding checklist with tasks
/// grouped by category and a progress summary. Tenant-scoped; the employee is resolved from ICurrentUser.
/// </summary>
public sealed record GetMyChecklistQuery : IRequest<Result<MyChecklistDto>>;

public sealed class GetMyChecklistQueryHandler : IRequestHandler<GetMyChecklistQuery, Result<MyChecklistDto>>
{
    private readonly IOnboardingChecklistService _service;

    public GetMyChecklistQueryHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<MyChecklistDto>> Handle(GetMyChecklistQuery request, CancellationToken cancellationToken)
        => _service.GetMyChecklistAsync(cancellationToken);
}
