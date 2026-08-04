using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-006 AC-K1: one scorecard by id, with its full edit history. Applies the same anti-bias rule
/// (FR-6/BR-5) as the summary views, so this cannot be used to read a scorecard the caller is not entitled
/// to see. Tenant-scoped.
/// </summary>
public sealed record GetScorecardByIdQuery(Guid ScorecardId) : IRequest<Result<ScorecardDetailDto>>;

public sealed class GetScorecardByIdQueryHandler
    : IRequestHandler<GetScorecardByIdQuery, Result<ScorecardDetailDto>>
{
    private readonly IScorecardService _service;

    public GetScorecardByIdQueryHandler(IScorecardService service) => _service = service;

    public Task<Result<ScorecardDetailDto>> Handle(GetScorecardByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.ScorecardId, cancellationToken);
}
