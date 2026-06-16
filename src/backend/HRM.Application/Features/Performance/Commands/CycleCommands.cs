using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Create ───────────────────────────────────────────────────────────────────

/// <summary>Creates an appraisal cycle with phases + resolved participants (US-PRF-004 AC-1/AC-2).</summary>
public sealed record CreateCycleCommand(CreateCycleInput Input) : IRequest<Result<CycleDto>>;

public sealed class CreateCycleCommandHandler : IRequestHandler<CreateCycleCommand, Result<CycleDto>>
{
    private readonly IAppraisalCycleService _service;
    public CreateCycleCommandHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result<CycleDto>> Handle(CreateCycleCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(request.Input, cancellationToken);
}

// ── Update / extend phase ────────────────────────────────────────────────────

/// <summary>Edits a cycle / extends a phase deadline; re-validates + reschedules + notifies (US-PRF-004 AC-5).</summary>
public sealed record UpdateCycleCommand(Guid CycleId, UpdateCycleInput Input) : IRequest<Result<CycleDto>>;

public sealed class UpdateCycleCommandHandler : IRequestHandler<UpdateCycleCommand, Result<CycleDto>>
{
    private readonly IAppraisalCycleService _service;
    public UpdateCycleCommandHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result<CycleDto>> Handle(UpdateCycleCommand request, CancellationToken cancellationToken)
        => _service.UpdateAsync(request.CycleId, request.Input, cancellationToken);
}

// ── Clone (FR-8) ─────────────────────────────────────────────────────────────

/// <summary>Clones an existing cycle as a template with new dates (US-PRF-004 FR-8).</summary>
public sealed record CloneCycleCommand(CloneCycleInput Input) : IRequest<Result<CycleDto>>;

public sealed class CloneCycleCommandHandler : IRequestHandler<CloneCycleCommand, Result<CycleDto>>
{
    private readonly IAppraisalCycleService _service;
    public CloneCycleCommandHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result<CycleDto>> Handle(CloneCycleCommand request, CancellationToken cancellationToken)
        => _service.CloneAsync(request.Input, cancellationToken);
}

// ── Status transition (FR-7) ─────────────────────────────────────────────────

/// <summary>Transitions a cycle's status per the FR-7 state machine (Cancel needs a reason, BR-6).</summary>
public sealed record TransitionCycleStatusCommand(
    Guid CycleId, AppraisalCycleStatus TargetStatus, string? Reason) : IRequest<Result<CycleDto>>;

public sealed class TransitionCycleStatusCommandHandler
    : IRequestHandler<TransitionCycleStatusCommand, Result<CycleDto>>
{
    private readonly IAppraisalCycleService _service;
    public TransitionCycleStatusCommandHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result<CycleDto>> Handle(TransitionCycleStatusCommand request, CancellationToken cancellationToken)
        => _service.TransitionStatusAsync(request.CycleId, request.TargetStatus, request.Reason, cancellationToken);
}

// ── Delete (Draft only, BR-2) ────────────────────────────────────────────────

/// <summary>Deletes a Draft cycle with no review activity (US-PRF-004 BR-2 — else cancel only).</summary>
public sealed record DeleteCycleCommand(Guid CycleId) : IRequest<Result>;

public sealed class DeleteCycleCommandHandler : IRequestHandler<DeleteCycleCommand, Result>
{
    private readonly IAppraisalCycleService _service;
    public DeleteCycleCommandHandler(IAppraisalCycleService service) => _service = service;

    public Task<Result> Handle(DeleteCycleCommand request, CancellationToken cancellationToken)
        => _service.DeleteAsync(request.CycleId, cancellationToken);
}
