using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-006 (AC-2/AC-3/FR-6): lists the scorecards for one interview the caller may see + the aggregate
/// average (FR-3). Applies the anti-bias rule (FR-6/BR-5). Tenant-scoped.
/// </summary>
public sealed record GetScorecardsForInterviewQuery(Guid InterviewId) : IRequest<Result<ScorecardSummaryDto>>;

public sealed class GetScorecardsForInterviewQueryHandler
    : IRequestHandler<GetScorecardsForInterviewQuery, Result<ScorecardSummaryDto>>
{
    private readonly IScorecardService _service;

    public GetScorecardsForInterviewQueryHandler(IScorecardService service) => _service = service;

    public Task<Result<ScorecardSummaryDto>> Handle(GetScorecardsForInterviewQuery request, CancellationToken cancellationToken)
        => _service.GetForInterviewAsync(request.InterviewId, cancellationToken);
}
