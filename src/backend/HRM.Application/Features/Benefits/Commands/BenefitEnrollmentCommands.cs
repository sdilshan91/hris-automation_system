using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Benefits.DTOs;
using MediatR;

namespace HRM.Application.Features.Benefits.Commands;

// ── Add eligibility rule (AC-1) ──────────────────────────────────────────────
public sealed record AddEligibilityRuleCommand(Guid PlanId, CreateEligibilityRuleRequest Request) : IRequest<Result<EligibilityRuleDto>>;

public sealed class AddEligibilityRuleCommandHandler : IRequestHandler<AddEligibilityRuleCommand, Result<EligibilityRuleDto>>
{
    private readonly IBenefitEnrollmentService _service;
    public AddEligibilityRuleCommandHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<EligibilityRuleDto>> Handle(AddEligibilityRuleCommand request, CancellationToken cancellationToken)
        => _service.AddRuleAsync(request.PlanId, request.Request, cancellationToken);
}

// ── Delete eligibility rule (AC-1) ───────────────────────────────────────────
public sealed record DeleteEligibilityRuleCommand(Guid RuleId) : IRequest<Result<bool>>;

public sealed class DeleteEligibilityRuleCommandHandler : IRequestHandler<DeleteEligibilityRuleCommand, Result<bool>>
{
    private readonly IBenefitEnrollmentService _service;
    public DeleteEligibilityRuleCommandHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<bool>> Handle(DeleteEligibilityRuleCommand request, CancellationToken cancellationToken)
        => _service.DeleteRuleAsync(request.RuleId, cancellationToken);
}

// ── Enroll (AC-3/AC-4/AC-5/AC-6) ─────────────────────────────────────────────
public sealed record EnrollBenefitCommand(EnrollRequest Request) : IRequest<Result<BenefitEnrollmentDto>>;

public sealed class EnrollBenefitCommandHandler : IRequestHandler<EnrollBenefitCommand, Result<BenefitEnrollmentDto>>
{
    private readonly IBenefitEnrollmentService _service;
    public EnrollBenefitCommandHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<BenefitEnrollmentDto>> Handle(EnrollBenefitCommand request, CancellationToken cancellationToken)
        => _service.EnrollAsync(request.Request, cancellationToken);
}

// ── Terminate enrollment (AC-7) ──────────────────────────────────────────────
public sealed record TerminateEnrollmentCommand(Guid EnrollmentId, TerminateEnrollmentRequest Request) : IRequest<Result<BenefitEnrollmentDto>>;

public sealed class TerminateEnrollmentCommandHandler : IRequestHandler<TerminateEnrollmentCommand, Result<BenefitEnrollmentDto>>
{
    private readonly IBenefitEnrollmentService _service;
    public TerminateEnrollmentCommandHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<BenefitEnrollmentDto>> Handle(TerminateEnrollmentCommand request, CancellationToken cancellationToken)
        => _service.TerminateAsync(request.EnrollmentId, request.Request, cancellationToken);
}
