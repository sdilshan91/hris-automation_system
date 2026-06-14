using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

// ════════════════════════════════════════════════════════════════════════
//  US-ATT-010 — HR dashboard + reports queries. Thin handlers delegate to
//  IAttendanceDashboardService (per the project CQRS convention).
// ════════════════════════════════════════════════════════════════════════

/// <summary>AC-1/FR-1: today's attendance KPIs for the date + scope (all|team).</summary>
public sealed record GetDashboardKpisQuery(DateOnly Date, string Scope)
    : IRequest<Result<DashboardKpiDto>>;

public sealed class GetDashboardKpisQueryHandler
    : IRequestHandler<GetDashboardKpisQuery, Result<DashboardKpiDto>>
{
    private readonly IAttendanceDashboardService _service;
    public GetDashboardKpisQueryHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<DashboardKpiDto>> Handle(GetDashboardKpisQuery request, CancellationToken cancellationToken)
        => _service.GetKpisAsync(request.Date, request.Scope, cancellationToken);
}

/// <summary>AC-2/FR-2: live attendance board for the date + scope (polling; SignalR deferred).</summary>
public sealed record GetLiveBoardQuery(DateOnly Date, string Scope)
    : IRequest<Result<LiveBoardResult>>;

public sealed class GetLiveBoardQueryHandler
    : IRequestHandler<GetLiveBoardQuery, Result<LiveBoardResult>>
{
    private readonly IAttendanceDashboardService _service;
    public GetLiveBoardQueryHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<LiveBoardResult>> Handle(GetLiveBoardQuery request, CancellationToken cancellationToken)
        => _service.GetLiveBoardAsync(request.Date, request.Scope, cancellationToken);
}

/// <summary>AC-3/FR-3: department attendance comparison for a month.</summary>
public sealed record GetDepartmentComparisonQuery(int Year, int Month)
    : IRequest<Result<DeptComparisonResult>>;

public sealed class GetDepartmentComparisonQueryHandler
    : IRequestHandler<GetDepartmentComparisonQuery, Result<DeptComparisonResult>>
{
    private readonly IAttendanceDashboardService _service;
    public GetDepartmentComparisonQueryHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<DeptComparisonResult>> Handle(GetDepartmentComparisonQuery request, CancellationToken cancellationToken)
        => _service.GetDepartmentComparisonAsync(request.Year, request.Month, cancellationToken);
}

/// <summary>AC-4/FR-4: custom date-range attendance report.</summary>
public sealed record GetCustomReportQuery(DateOnly From, DateOnly To, CustomReportFilter Filter)
    : IRequest<Result<CustomReportResult>>;

public sealed class GetCustomReportQueryHandler
    : IRequestHandler<GetCustomReportQuery, Result<CustomReportResult>>
{
    private readonly IAttendanceDashboardService _service;
    public GetCustomReportQueryHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<CustomReportResult>> Handle(GetCustomReportQuery request, CancellationToken cancellationToken)
        => _service.GetCustomReportAsync(request.From, request.To, request.Filter, cancellationToken);
}

/// <summary>FR-5: custom report export (csv|xlsx|pdf).</summary>
public sealed record ExportCustomReportQuery(DateOnly From, DateOnly To, string Format, CustomReportFilter Filter)
    : IRequest<Result<CustomReportExportResult>>;

public sealed class ExportCustomReportQueryHandler
    : IRequestHandler<ExportCustomReportQuery, Result<CustomReportExportResult>>
{
    private readonly IAttendanceDashboardService _service;
    public ExportCustomReportQueryHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<CustomReportExportResult>> Handle(ExportCustomReportQuery request, CancellationToken cancellationToken)
        => _service.ExportCustomReportAsync(request.From, request.To, request.Format, request.Filter, cancellationToken);
}

/// <summary>AC-5/FR-6/BR-5: 12-month trend analytics from the monthly summary table.</summary>
public sealed record GetTrendsQuery(int Months) : IRequest<Result<TrendsResult>>;

public sealed class GetTrendsQueryHandler : IRequestHandler<GetTrendsQuery, Result<TrendsResult>>
{
    private readonly IAttendanceDashboardService _service;
    public GetTrendsQueryHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<TrendsResult>> Handle(GetTrendsQuery request, CancellationToken cancellationToken)
        => _service.GetTrendsAsync(request.Months, cancellationToken);
}

/// <summary>FR-8: list the tenant's scheduled report configs.</summary>
public sealed record GetScheduledReportsQuery : IRequest<Result<IReadOnlyList<ScheduledReportConfigDto>>>;

public sealed class GetScheduledReportsQueryHandler
    : IRequestHandler<GetScheduledReportsQuery, Result<IReadOnlyList<ScheduledReportConfigDto>>>
{
    private readonly IAttendanceDashboardService _service;
    public GetScheduledReportsQueryHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<IReadOnlyList<ScheduledReportConfigDto>>> Handle(
        GetScheduledReportsQuery request, CancellationToken cancellationToken)
        => _service.GetScheduledReportsAsync(cancellationToken);
}
