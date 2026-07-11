using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Benefits.DTOs;
using MediatR;

namespace HRM.Application.Features.Benefits.Commands;

// ── Create plan (AC-1) ───────────────────────────────────────────────────────
public sealed record CreateBenefitPlanCommand(CreateBenefitPlanRequest Request) : IRequest<Result<BenefitPlanDto>>;

public sealed class CreateBenefitPlanCommandHandler : IRequestHandler<CreateBenefitPlanCommand, Result<BenefitPlanDto>>
{
    private readonly IBenefitPlanService _service;
    public CreateBenefitPlanCommandHandler(IBenefitPlanService service) => _service = service;

    public Task<Result<BenefitPlanDto>> Handle(CreateBenefitPlanCommand request, CancellationToken cancellationToken)
        => _service.CreatePlanAsync(request.Request, cancellationToken);
}

// ── Update plan metadata (AC-4) ──────────────────────────────────────────────
public sealed record UpdateBenefitPlanCommand(Guid PlanId, UpdateBenefitPlanRequest Request) : IRequest<Result<BenefitPlanDto>>;

public sealed class UpdateBenefitPlanCommandHandler : IRequestHandler<UpdateBenefitPlanCommand, Result<BenefitPlanDto>>
{
    private readonly IBenefitPlanService _service;
    public UpdateBenefitPlanCommandHandler(IBenefitPlanService service) => _service = service;

    public Task<Result<BenefitPlanDto>> Handle(UpdateBenefitPlanCommand request, CancellationToken cancellationToken)
        => _service.UpdatePlanAsync(request.PlanId, request.Request, cancellationToken);
}

// ── Change plan status (AC-2/AC-3/AC-6) ──────────────────────────────────────
public sealed record ChangeBenefitPlanStatusCommand(Guid PlanId, string Status) : IRequest<Result<BenefitPlanDto>>;

public sealed class ChangeBenefitPlanStatusCommandHandler : IRequestHandler<ChangeBenefitPlanStatusCommand, Result<BenefitPlanDto>>
{
    private readonly IBenefitPlanService _service;
    public ChangeBenefitPlanStatusCommandHandler(IBenefitPlanService service) => _service = service;

    public Task<Result<BenefitPlanDto>> Handle(ChangeBenefitPlanStatusCommand request, CancellationToken cancellationToken)
        => _service.ChangeStatusAsync(request.PlanId, request.Status, cancellationToken);
}
