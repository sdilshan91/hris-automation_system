using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.SubscriptionPlans.DTOs;
using MediatR;

namespace HRM.Application.Features.SubscriptionPlans.Commands;

// ── Create (AC-2) ───────────────────────────────────────────────────────────

/// <summary>Creates a subscription plan (US-ADM-009 AC-2/FR-2/FR-3). <c>Code</c> is set once and immutable.</summary>
public sealed record CreatePlanCommand(string Code, PlanEditableFields Fields)
    : IRequest<Result<CreatePlanResultDto>>;

public sealed class CreatePlanCommandHandler
    : IRequestHandler<CreatePlanCommand, Result<CreatePlanResultDto>>
{
    private readonly ISubscriptionPlanService _service;
    public CreatePlanCommandHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result<CreatePlanResultDto>> Handle(CreatePlanCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(request.Code, request.Fields, cancellationToken);
}

// ── Update (AC-3) — code is immutable, never accepted here ────────────────────

/// <summary>Updates a plan's editable fields (US-ADM-009 AC-3). The code is IMMUTABLE and is not part of the command.</summary>
public sealed record UpdatePlanCommand(Guid Id, PlanEditableFields Fields) : IRequest<Result>;

public sealed class UpdatePlanCommandHandler : IRequestHandler<UpdatePlanCommand, Result>
{
    private readonly ISubscriptionPlanService _service;
    public UpdatePlanCommandHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result> Handle(UpdatePlanCommand request, CancellationToken cancellationToken)
        => _service.UpdateAsync(request.Id, request.Fields, cancellationToken);
}

// ── Archive (AC-4) ────────────────────────────────────────────────────────────

/// <summary>Archives a plan: IsActive=false; no longer offered for new tenants (US-ADM-009 AC-4).</summary>
public sealed record ArchivePlanCommand(Guid Id) : IRequest<Result>;

public sealed class ArchivePlanCommandHandler : IRequestHandler<ArchivePlanCommand, Result>
{
    private readonly ISubscriptionPlanService _service;
    public ArchivePlanCommandHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result> Handle(ArchivePlanCommand request, CancellationToken cancellationToken)
        => _service.ArchiveAsync(request.Id, cancellationToken);
}

// ── Delete (FR-7/BR-5) — rejected when referenced ─────────────────────────────

/// <summary>Deletes a plan; rejected if any tenant references it (US-ADM-009 FR-7/BR-5).</summary>
public sealed record DeletePlanCommand(Guid Id) : IRequest<Result>;

public sealed class DeletePlanCommandHandler : IRequestHandler<DeletePlanCommand, Result>
{
    private readonly ISubscriptionPlanService _service;
    public DeletePlanCommandHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result> Handle(DeletePlanCommand request, CancellationToken cancellationToken)
        => _service.DeleteAsync(request.Id, cancellationToken);
}

// ── Plan limit overrides (AC-5/FR-4) ──────────────────────────────────────────

/// <summary>Creates/updates a per-tenant plan-limit override (US-ADM-009 AC-5/FR-4).</summary>
public sealed record UpsertPlanLimitOverrideCommand(
    Guid TenantId, string LimitKey, long? Value, DateTime? ExpiresAt)
    : IRequest<Result<PlanLimitOverrideDto>>;

public sealed class UpsertPlanLimitOverrideCommandHandler
    : IRequestHandler<UpsertPlanLimitOverrideCommand, Result<PlanLimitOverrideDto>>
{
    private readonly ISubscriptionPlanService _service;
    public UpsertPlanLimitOverrideCommandHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result<PlanLimitOverrideDto>> Handle(
        UpsertPlanLimitOverrideCommand request, CancellationToken cancellationToken)
        => _service.UpsertOverrideAsync(
            request.TenantId, request.LimitKey, request.Value, request.ExpiresAt, cancellationToken);
}

/// <summary>Removes a per-tenant plan-limit override (US-ADM-009 AC-5).</summary>
public sealed record RemovePlanLimitOverrideCommand(Guid OverrideId) : IRequest<Result>;

public sealed class RemovePlanLimitOverrideCommandHandler
    : IRequestHandler<RemovePlanLimitOverrideCommand, Result>
{
    private readonly ISubscriptionPlanService _service;
    public RemovePlanLimitOverrideCommandHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result> Handle(RemovePlanLimitOverrideCommand request, CancellationToken cancellationToken)
        => _service.RemoveOverrideAsync(request.OverrideId, cancellationToken);
}
