using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>Gets all goals for an employee within a cycle + the weight total (US-PRF-001 AC-2/AC-3).</summary>
public sealed record GetEmployeeGoalsQuery(Guid EmployeeId, Guid CycleId)
    : IRequest<Result<EmployeeGoalsDto>>;

public sealed class GetEmployeeGoalsQueryHandler
    : IRequestHandler<GetEmployeeGoalsQuery, Result<EmployeeGoalsDto>>
{
    private readonly IGoalService _service;
    public GetEmployeeGoalsQueryHandler(IGoalService service) => _service = service;

    public Task<Result<EmployeeGoalsDto>> Handle(GetEmployeeGoalsQuery request, CancellationToken cancellationToken)
        => _service.GetEmployeeGoalsAsync(request.EmployeeId, request.CycleId, cancellationToken);
}

/// <summary>Gets the calling manager's team goals dashboard for a cycle (US-PRF-001 AC-4).</summary>
public sealed record GetTeamGoalsDashboardQuery(Guid CycleId)
    : IRequest<Result<TeamGoalsDashboardDto>>;

public sealed class GetTeamGoalsDashboardQueryHandler
    : IRequestHandler<GetTeamGoalsDashboardQuery, Result<TeamGoalsDashboardDto>>
{
    private readonly IGoalService _service;
    public GetTeamGoalsDashboardQueryHandler(IGoalService service) => _service = service;

    public Task<Result<TeamGoalsDashboardDto>> Handle(GetTeamGoalsDashboardQuery request, CancellationToken cancellationToken)
        => _service.GetTeamDashboardAsync(request.CycleId, cancellationToken);
}
