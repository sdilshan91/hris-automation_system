using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

// ════════════════════════════════════════════════════════════════════════
//  US-REC-009 — Recruitment dashboard + analytics queries. Thin handlers
//  delegate to IRecruitmentDashboardService (per the project CQRS convention).
// ════════════════════════════════════════════════════════════════════════

/// <summary>AC-1..AC-4/FR-1..FR-5/FR-9: the full recruitment dashboard for the filter.</summary>
public sealed record RecruitmentDashboardQuery(RecruitmentDashboardFilter Filter)
    : IRequest<Result<RecruitmentDashboardDto>>;

public sealed class RecruitmentDashboardQueryHandler
    : IRequestHandler<RecruitmentDashboardQuery, Result<RecruitmentDashboardDto>>
{
    private readonly IRecruitmentDashboardService _service;
    public RecruitmentDashboardQueryHandler(IRecruitmentDashboardService service) => _service = service;

    public Task<Result<RecruitmentDashboardDto>> Handle(
        RecruitmentDashboardQuery request, CancellationToken cancellationToken)
        => _service.GetDashboardAsync(request.Filter, cancellationToken);
}

/// <summary>FR-8: recruitment dashboard export (csv|xlsx).</summary>
public sealed record ExportRecruitmentDashboardQuery(RecruitmentDashboardFilter Filter, string Format)
    : IRequest<Result<RecruitmentDashboardExportResult>>;

public sealed class ExportRecruitmentDashboardQueryHandler
    : IRequestHandler<ExportRecruitmentDashboardQuery, Result<RecruitmentDashboardExportResult>>
{
    private readonly IRecruitmentDashboardService _service;
    public ExportRecruitmentDashboardQueryHandler(IRecruitmentDashboardService service) => _service = service;

    public Task<Result<RecruitmentDashboardExportResult>> Handle(
        ExportRecruitmentDashboardQuery request, CancellationToken cancellationToken)
        => _service.ExportDashboardAsync(request.Filter, request.Format, cancellationToken);
}
