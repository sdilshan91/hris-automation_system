using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-006 (FR-1/BR-2): returns the default interview evaluation criteria the scorecard form renders.
/// Tenant-agnostic in Phase 1 (per-tenant config S35.2.9 is deferred).
/// </summary>
public sealed record GetScorecardCriteriaQuery() : IRequest<Result<IReadOnlyList<ScorecardCriterionDto>>>;

public sealed class GetScorecardCriteriaQueryHandler
    : IRequestHandler<GetScorecardCriteriaQuery, Result<IReadOnlyList<ScorecardCriterionDto>>>
{
    private readonly IScorecardService _service;

    public GetScorecardCriteriaQueryHandler(IScorecardService service) => _service = service;

    public Task<Result<IReadOnlyList<ScorecardCriterionDto>>> Handle(GetScorecardCriteriaQuery request, CancellationToken cancellationToken)
        => Task.FromResult(_service.GetCriteria());
}
