using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>
/// US-ONB-003 AC-1/FR-4: the cheap progress summary for the logged-in employee's dashboard widget.
/// Tenant-scoped; the employee is resolved from ICurrentUser.
/// </summary>
public sealed record GetMyChecklistProgressQuery : IRequest<Result<MyChecklistProgressDto>>;

public sealed class GetMyChecklistProgressQueryHandler
    : IRequestHandler<GetMyChecklistProgressQuery, Result<MyChecklistProgressDto>>
{
    private readonly IOnboardingChecklistService _service;

    public GetMyChecklistProgressQueryHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<MyChecklistProgressDto>> Handle(
        GetMyChecklistProgressQuery request, CancellationToken cancellationToken)
        => _service.GetMyChecklistProgressAsync(cancellationToken);
}
