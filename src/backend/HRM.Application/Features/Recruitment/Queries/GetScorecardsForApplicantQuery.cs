using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-006 (AC-2/AC-3/FR-6): lists the scorecards across all of an applicant's interviews the caller may
/// see + the aggregate average (FR-3). Applies the anti-bias rule (FR-6/BR-5). Tenant-scoped.
/// </summary>
public sealed record GetScorecardsForApplicantQuery(Guid ApplicantId) : IRequest<Result<ScorecardSummaryDto>>;

public sealed class GetScorecardsForApplicantQueryHandler
    : IRequestHandler<GetScorecardsForApplicantQuery, Result<ScorecardSummaryDto>>
{
    private readonly IScorecardService _service;

    public GetScorecardsForApplicantQueryHandler(IScorecardService service) => _service = service;

    public Task<Result<ScorecardSummaryDto>> Handle(GetScorecardsForApplicantQuery request, CancellationToken cancellationToken)
        => _service.GetForApplicantAsync(request.ApplicantId, cancellationToken);
}
