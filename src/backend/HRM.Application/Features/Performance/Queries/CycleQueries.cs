using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>Returns a single cycle with its phases (US-PRF-004 §7).</summary>
public sealed record GetCycleByIdQuery(Guid CycleId) : IRequest<Result<CycleDto>>;

public sealed class GetCycleByIdQueryHandler : IRequestHandler<GetCycleByIdQuery, Result<CycleDto>>
{
    private readonly IAppraisalCycleService _service;
    public GetCycleByIdQueryHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result<CycleDto>> Handle(GetCycleByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.CycleId, cancellationToken);
}

/// <summary>Lists cycles for the tenant, optionally filtered by status (US-PRF-004).</summary>
public sealed record ListCyclesQuery(AppraisalCycleStatus? Status) : IRequest<Result<IReadOnlyList<CycleSummaryDto>>>;

public sealed class ListCyclesQueryHandler : IRequestHandler<ListCyclesQuery, Result<IReadOnlyList<CycleSummaryDto>>>
{
    private readonly IAppraisalCycleService _service;
    public ListCyclesQueryHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result<IReadOnlyList<CycleSummaryDto>>> Handle(ListCyclesQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.Status, cancellationToken);
}

/// <summary>The canonical active-cycle-with-current-phase + authoritative window flags (US-PRF-004 AC-3).</summary>
public sealed record GetActiveCycleQuery : IRequest<Result<ActiveCycleDto>>;

public sealed class GetActiveCycleQueryHandler : IRequestHandler<GetActiveCycleQuery, Result<ActiveCycleDto>>
{
    private readonly IAppraisalCycleService _service;
    public GetActiveCycleQueryHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result<ActiveCycleDto>> Handle(GetActiveCycleQuery request, CancellationToken cancellationToken)
        => _service.GetActiveAsync(cancellationToken);
}

/// <summary>The cycle dashboard: timeline + per-phase completion stats + overdue counts (US-PRF-004 AC-3).</summary>
public sealed record GetCycleDashboardQuery(Guid CycleId) : IRequest<Result<CycleDashboardDto>>;

public sealed class GetCycleDashboardQueryHandler : IRequestHandler<GetCycleDashboardQuery, Result<CycleDashboardDto>>
{
    private readonly IAppraisalCycleService _service;
    public GetCycleDashboardQueryHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result<CycleDashboardDto>> Handle(GetCycleDashboardQuery request, CancellationToken cancellationToken)
        => _service.GetDashboardAsync(request.CycleId, cancellationToken);
}
