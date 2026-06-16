using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

// ── Workspace (AC-1, NFR-4) ─────────────────────────────────────────────

/// <summary>The recommendation workspace for a completed cycle (US-PRF-010 AC-1). HR (all) / manager (reports).</summary>
public sealed record RecommendationWorkspaceQuery(RecommendationWorkspaceQueryInput Input)
    : IRequest<Result<RecommendationWorkspaceDto>>;

public sealed class RecommendationWorkspaceQueryHandler
    : IRequestHandler<RecommendationWorkspaceQuery, Result<RecommendationWorkspaceDto>>
{
    private readonly IRecommendationService _service;
    public RecommendationWorkspaceQueryHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationWorkspaceDto>> Handle(RecommendationWorkspaceQuery request, CancellationToken cancellationToken)
        => _service.GetWorkspaceAsync(request.Input, cancellationToken);
}

// ── Single recommendation ───────────────────────────────────────────────

/// <summary>One full recommendation (US-PRF-010 AC-5 scope-restricted).</summary>
public sealed record GetRecommendationQuery(Guid RecommendationId) : IRequest<Result<RecommendationDto>>;

public sealed class GetRecommendationQueryHandler : IRequestHandler<GetRecommendationQuery, Result<RecommendationDto>>
{
    private readonly IRecommendationService _service;
    public GetRecommendationQueryHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationDto>> Handle(GetRecommendationQuery request, CancellationToken cancellationToken)
        => _service.GetAsync(request.RecommendationId, cancellationToken);
}

// ── Summary stats (AC-4/FR-6) ───────────────────────────────────────────

/// <summary>Aggregate recommendation summary stats for the cycle (US-PRF-010 AC-4). HR-only.</summary>
public sealed record RecommendationSummaryQuery(Guid? CycleId) : IRequest<Result<RecommendationSummaryDto>>;

public sealed class RecommendationSummaryQueryHandler
    : IRequestHandler<RecommendationSummaryQuery, Result<RecommendationSummaryDto>>
{
    private readonly IRecommendationService _service;
    public RecommendationSummaryQueryHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationSummaryDto>> Handle(RecommendationSummaryQuery request, CancellationToken cancellationToken)
        => _service.GetSummaryAsync(request.CycleId, cancellationToken);
}

/// <summary>Exports the recommendation summary as CSV/XLSX (US-PRF-010 FR-6). HR-only.</summary>
public sealed record ExportRecommendationSummaryQuery(Guid? CycleId, string Format)
    : IRequest<Result<RecommendationExportResult>>;

public sealed class ExportRecommendationSummaryQueryHandler
    : IRequestHandler<ExportRecommendationSummaryQuery, Result<RecommendationExportResult>>
{
    private readonly IRecommendationService _service;
    public ExportRecommendationSummaryQueryHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationExportResult>> Handle(ExportRecommendationSummaryQuery request, CancellationToken cancellationToken)
        => _service.ExportSummaryAsync(request.CycleId, request.Format, cancellationToken);
}

// ── Budget + rules config reads (FR-8/FR-2) ─────────────────────────────

/// <summary>The cycle's budget tracker (US-PRF-010 FR-8/BR-4). HR-only.</summary>
public sealed record GetBudgetQuery(Guid CycleId) : IRequest<Result<RecommendationBudgetDto?>>;

public sealed class GetBudgetQueryHandler : IRequestHandler<GetBudgetQuery, Result<RecommendationBudgetDto?>>
{
    private readonly IRecommendationService _service;
    public GetBudgetQueryHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationBudgetDto?>> Handle(GetBudgetQuery request, CancellationToken cancellationToken)
        => _service.GetBudgetAsync(request.CycleId, cancellationToken);
}

/// <summary>The tenant's auto-generation rules (US-PRF-010 FR-2). HR-only.</summary>
public sealed record ListRulesQuery : IRequest<Result<IReadOnlyList<RecommendationRuleDto>>>;

public sealed class ListRulesQueryHandler : IRequestHandler<ListRulesQuery, Result<IReadOnlyList<RecommendationRuleDto>>>
{
    private readonly IRecommendationService _service;
    public ListRulesQueryHandler(IRecommendationService service) => _service = service;

    public Task<Result<IReadOnlyList<RecommendationRuleDto>>> Handle(ListRulesQuery request, CancellationToken cancellationToken)
        => _service.ListRulesAsync(cancellationToken);
}
