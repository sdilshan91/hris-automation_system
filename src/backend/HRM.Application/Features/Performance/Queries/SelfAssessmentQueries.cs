using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>
/// Gets the calling employee's own self-assessment for a cycle, with all goals + any saved ratings
/// (US-PRF-002 AC-1). The employee is resolved from the authenticated caller — never supplied (NFR-2).
/// </summary>
public sealed record GetMySelfAssessmentQuery(Guid CycleId) : IRequest<Result<SelfAssessmentDto>>;

public sealed class GetMySelfAssessmentQueryHandler
    : IRequestHandler<GetMySelfAssessmentQuery, Result<SelfAssessmentDto>>
{
    private readonly ISelfAssessmentService _service;
    public GetMySelfAssessmentQueryHandler(ISelfAssessmentService service) => _service = service;

    public Task<Result<SelfAssessmentDto>> Handle(GetMySelfAssessmentQuery request, CancellationToken cancellationToken)
        => _service.GetMyAssessmentAsync(request.CycleId, cancellationToken);
}
