using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>
/// Gets the manager's review workspace for an employee + cycle (US-PRF-003 AC-1): each goal with the
/// employee's self-rating/comment alongside the manager's own rating/comment.
/// </summary>
public sealed record GetManagerReviewWorkspaceQuery(Guid EmployeeId, Guid CycleId)
    : IRequest<Result<ManagerReviewDto>>;

public sealed class GetManagerReviewWorkspaceQueryHandler
    : IRequestHandler<GetManagerReviewWorkspaceQuery, Result<ManagerReviewDto>>
{
    private readonly IManagerReviewService _service;
    public GetManagerReviewWorkspaceQueryHandler(IManagerReviewService service) => _service = service;

    public Task<Result<ManagerReviewDto>> Handle(GetManagerReviewWorkspaceQuery request, CancellationToken cancellationToken)
        => _service.GetReviewWorkspaceAsync(request.EmployeeId, request.CycleId, cancellationToken);
}

/// <summary>Gets the team-reviews dashboard for the calling manager + cycle (US-PRF-003 AC-4).</summary>
public sealed record GetTeamReviewsDashboardQuery(Guid CycleId)
    : IRequest<Result<TeamReviewsDashboardDto>>;

public sealed class GetTeamReviewsDashboardQueryHandler
    : IRequestHandler<GetTeamReviewsDashboardQuery, Result<TeamReviewsDashboardDto>>
{
    private readonly IManagerReviewService _service;
    public GetTeamReviewsDashboardQueryHandler(IManagerReviewService service) => _service = service;

    public Task<Result<TeamReviewsDashboardDto>> Handle(GetTeamReviewsDashboardQuery request, CancellationToken cancellationToken)
        => _service.GetTeamReviewsAsync(request.CycleId, cancellationToken);
}
