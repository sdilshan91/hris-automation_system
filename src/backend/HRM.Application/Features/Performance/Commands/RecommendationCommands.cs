using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Auto-generate suggestions (AC-2/FR-2/BR-3) ──────────────────────────

/// <summary>Auto-generates recommendation suggestions across a cycle (US-PRF-010 AC-2). HR-only.</summary>
public sealed record AutoGenerateRecommendationsCommand(Guid CycleId) : IRequest<Result<AutoGenerateResultDto>>;

public sealed class AutoGenerateRecommendationsCommandHandler
    : IRequestHandler<AutoGenerateRecommendationsCommand, Result<AutoGenerateResultDto>>
{
    private readonly IRecommendationService _service;
    public AutoGenerateRecommendationsCommandHandler(IRecommendationService service) => _service = service;

    public Task<Result<AutoGenerateResultDto>> Handle(AutoGenerateRecommendationsCommand request, CancellationToken cancellationToken)
        => _service.AutoGenerateAsync(request.CycleId, cancellationToken);
}

// ── Manual create / override (FR-1/FR-3/BR-5) ───────────────────────────

/// <summary>Creates or overrides a recommendation (US-PRF-010 FR-1/FR-3). HR-only.</summary>
public sealed record SaveRecommendationCommand(SaveRecommendationInput Input) : IRequest<Result<RecommendationDto>>;

public sealed class SaveRecommendationCommandHandler : IRequestHandler<SaveRecommendationCommand, Result<RecommendationDto>>
{
    private readonly IRecommendationService _service;
    public SaveRecommendationCommandHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationDto>> Handle(SaveRecommendationCommand request, CancellationToken cancellationToken)
        => _service.SaveAsync(request.Input, cancellationToken);
}

// ── Submit into the approval workflow (AC-3/FR-4) ───────────────────────

/// <summary>Submits a recommendation into the approval workflow (US-PRF-010 AC-3/FR-4). HR-only.</summary>
public sealed record SubmitRecommendationCommand(SubmitRecommendationInput Input) : IRequest<Result<RecommendationDto>>;

public sealed class SubmitRecommendationCommandHandler : IRequestHandler<SubmitRecommendationCommand, Result<RecommendationDto>>
{
    private readonly IRecommendationService _service;
    public SubmitRecommendationCommandHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationDto>> Handle(SubmitRecommendationCommand request, CancellationToken cancellationToken)
        => _service.SubmitAsync(request.Input, cancellationToken);
}

// ── Approve / reject (FR-4) ─────────────────────────────────────────────

/// <summary>An approver approves or rejects a recommendation step (US-PRF-010 FR-4).</summary>
public sealed record DecideRecommendationCommand(DecideRecommendationInput Input) : IRequest<Result<RecommendationDto>>;

public sealed class DecideRecommendationCommandHandler : IRequestHandler<DecideRecommendationCommand, Result<RecommendationDto>>
{
    private readonly IRecommendationService _service;
    public DecideRecommendationCommandHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationDto>> Handle(DecideRecommendationCommand request, CancellationToken cancellationToken)
        => _service.DecideAsync(request.Input, cancellationToken);
}

// ── Budget config (FR-8) ────────────────────────────────────────────────

/// <summary>Creates or updates the cycle's bonus/increment budget (US-PRF-010 FR-8). HR-only.</summary>
public sealed record SaveBudgetCommand(SaveBudgetInput Input) : IRequest<Result<RecommendationBudgetDto>>;

public sealed class SaveBudgetCommandHandler : IRequestHandler<SaveBudgetCommand, Result<RecommendationBudgetDto>>
{
    private readonly IRecommendationService _service;
    public SaveBudgetCommandHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationBudgetDto>> Handle(SaveBudgetCommand request, CancellationToken cancellationToken)
        => _service.SaveBudgetAsync(request.Input, cancellationToken);
}

// ── Rule config (FR-2) ──────────────────────────────────────────────────

/// <summary>Creates or updates an auto-generation rule (US-PRF-010 FR-2). HR-only.</summary>
public sealed record SaveRuleCommand(SaveRuleInput Input) : IRequest<Result<RecommendationRuleDto>>;

public sealed class SaveRuleCommandHandler : IRequestHandler<SaveRuleCommand, Result<RecommendationRuleDto>>
{
    private readonly IRecommendationService _service;
    public SaveRuleCommandHandler(IRecommendationService service) => _service = service;

    public Task<Result<RecommendationRuleDto>> Handle(SaveRuleCommand request, CancellationToken cancellationToken)
        => _service.SaveRuleAsync(request.Input, cancellationToken);
}
