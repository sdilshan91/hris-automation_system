using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>Lists the calling employee's active goals with current progress (US-PRF-009 AC-1).</summary>
public sealed record GetMyGoalsQuery : IRequest<Result<IReadOnlyList<MyGoalProgressDto>>>;

public sealed class GetMyGoalsQueryHandler : IRequestHandler<GetMyGoalsQuery, Result<IReadOnlyList<MyGoalProgressDto>>>
{
    private readonly IGoalProgressService _service;
    public GetMyGoalsQueryHandler(IGoalProgressService service) => _service = service;

    public Task<Result<IReadOnlyList<MyGoalProgressDto>>> Handle(GetMyGoalsQuery request, CancellationToken cancellationToken)
        => _service.GetMyGoalsAsync(cancellationToken);
}

/// <summary>Returns one goal's chronological progress timeline + comment thread (US-PRF-009 AC-3/FR-3/FR-8).</summary>
public sealed record GetGoalTimelineQuery(Guid GoalId) : IRequest<Result<GoalTimelineDto>>;

public sealed class GetGoalTimelineQueryHandler : IRequestHandler<GetGoalTimelineQuery, Result<GoalTimelineDto>>
{
    private readonly IGoalProgressService _service;
    public GetGoalTimelineQueryHandler(IGoalProgressService service) => _service = service;

    public Task<Result<GoalTimelineDto>> Handle(GetGoalTimelineQuery request, CancellationToken cancellationToken)
        => _service.GetGoalTimelineAsync(request.GoalId, cancellationToken);
}

/// <summary>Manager team goal-progress summary, scoped to direct reports / all (US-PRF-009 AC-4/FR-4).</summary>
public sealed record GetTeamGoalSummaryQuery : IRequest<Result<IReadOnlyList<TeamGoalProgressRowDto>>>;

public sealed class GetTeamGoalSummaryQueryHandler
    : IRequestHandler<GetTeamGoalSummaryQuery, Result<IReadOnlyList<TeamGoalProgressRowDto>>>
{
    private readonly IGoalProgressService _service;
    public GetTeamGoalSummaryQueryHandler(IGoalProgressService service) => _service = service;

    public Task<Result<IReadOnlyList<TeamGoalProgressRowDto>>> Handle(
        GetTeamGoalSummaryQuery request, CancellationToken cancellationToken)
        => _service.GetTeamSummaryAsync(cancellationToken);
}

/// <summary>Drill-down: one in-scope employee's goals with current progress (US-PRF-009 AC-4).</summary>
public sealed record GetEmployeeGoalProgressQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<MyGoalProgressDto>>>;

public sealed class GetEmployeeGoalProgressQueryHandler
    : IRequestHandler<GetEmployeeGoalProgressQuery, Result<IReadOnlyList<MyGoalProgressDto>>>
{
    private readonly IGoalProgressService _service;
    public GetEmployeeGoalProgressQueryHandler(IGoalProgressService service) => _service = service;

    public Task<Result<IReadOnlyList<MyGoalProgressDto>>> Handle(
        GetEmployeeGoalProgressQuery request, CancellationToken cancellationToken)
        => _service.GetEmployeeGoalsAsync(request.EmployeeId, cancellationToken);
}
