using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

// ════════════════════════════════════════════════════════════════════════
//  US-PRF-007 — Performance Dashboard and Analytics query handlers.
//  Thin MediatR handlers that delegate to IPerformanceDashboardService. The service owns the
//  tenant-scoped aggregation + the HR-vs-manager scope enforcement (BR-1/BR-3/AC-5).
// ════════════════════════════════════════════════════════════════════════

/// <summary>AC-1/AC-2/FR-1..FR-3/FR-6: the dashboard overview, scoped to the caller.</summary>
public sealed record PerformanceDashboardOverviewQuery(PerformanceDashboardFilter Filter)
    : IRequest<Result<PerformanceDashboardDto>>;

public sealed class PerformanceDashboardOverviewQueryHandler
    : IRequestHandler<PerformanceDashboardOverviewQuery, Result<PerformanceDashboardDto>>
{
    private readonly IPerformanceDashboardService _service;
    public PerformanceDashboardOverviewQueryHandler(IPerformanceDashboardService service) => _service = service;

    public Task<Result<PerformanceDashboardDto>> Handle(
        PerformanceDashboardOverviewQuery request, CancellationToken cancellationToken)
        => _service.GetOverviewAsync(request.Filter, cancellationToken);
}

/// <summary>FR-5/AC-2: department drill-down — the department's employees + individual scores.</summary>
public sealed record PerformanceDepartmentDrilldownQuery(Guid DepartmentId, PerformanceDashboardFilter Filter)
    : IRequest<Result<DepartmentDrilldownDto>>;

public sealed class PerformanceDepartmentDrilldownQueryHandler
    : IRequestHandler<PerformanceDepartmentDrilldownQuery, Result<DepartmentDrilldownDto>>
{
    private readonly IPerformanceDashboardService _service;
    public PerformanceDepartmentDrilldownQueryHandler(IPerformanceDashboardService service) => _service = service;

    public Task<Result<DepartmentDrilldownDto>> Handle(
        PerformanceDepartmentDrilldownQuery request, CancellationToken cancellationToken)
        => _service.GetDepartmentDrilldownAsync(request.DepartmentId, request.Filter, cancellationToken);
}

/// <summary>AC-3/FR-7: multi-cycle average-score trend, optionally with a per-department overlay.</summary>
public sealed record PerformanceTrendQuery(
    IReadOnlyList<Guid> CycleIds, PerformanceDashboardFilter Filter, bool IncludeDepartmentSeries)
    : IRequest<Result<PerformanceTrendDto>>;

public sealed class PerformanceTrendQueryHandler
    : IRequestHandler<PerformanceTrendQuery, Result<PerformanceTrendDto>>
{
    private readonly IPerformanceDashboardService _service;
    public PerformanceTrendQueryHandler(IPerformanceDashboardService service) => _service = service;

    public Task<Result<PerformanceTrendDto>> Handle(
        PerformanceTrendQuery request, CancellationToken cancellationToken)
        => _service.GetTrendAsync(request.CycleIds, request.Filter, request.IncludeDepartmentSeries, cancellationToken);
}

/// <summary>FR-8/AC-4: export the overview's tabular data as a CSV/XLSX download.</summary>
public sealed record ExportPerformanceDashboardQuery(PerformanceDashboardFilter Filter, string Format)
    : IRequest<Result<PerformanceDashboardExportResult>>;

public sealed class ExportPerformanceDashboardQueryHandler
    : IRequestHandler<ExportPerformanceDashboardQuery, Result<PerformanceDashboardExportResult>>
{
    private readonly IPerformanceDashboardService _service;
    public ExportPerformanceDashboardQueryHandler(IPerformanceDashboardService service) => _service = service;

    public Task<Result<PerformanceDashboardExportResult>> Handle(
        ExportPerformanceDashboardQuery request, CancellationToken cancellationToken)
        => _service.ExportOverviewAsync(request.Filter, request.Format, cancellationToken);
}
