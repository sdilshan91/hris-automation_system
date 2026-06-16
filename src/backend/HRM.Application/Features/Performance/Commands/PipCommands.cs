using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Create PIP (AC-1/AC-2/FR-1, HR-only BR-1) ───────────────────────────

/// <summary>Creates &amp; initiates a Performance Improvement Plan (US-PRF-008 AC-1/AC-2). HR-only (BR-1).</summary>
public sealed record CreatePipCommand(CreatePipInput Input) : IRequest<Result<PipDto>>;

public sealed class CreatePipCommandHandler : IRequestHandler<CreatePipCommand, Result<PipDto>>
{
    private readonly IPipService _service;
    public CreatePipCommandHandler(IPipService service) => _service = service;

    public Task<Result<PipDto>> Handle(CreatePipCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(request.Input, cancellationToken);
}

// ── Employee acknowledge (BR-4) ─────────────────────────────────────────

/// <summary>Employee acknowledges PIP initiation (US-PRF-008 BR-4 — mirrors US-PRF-006 sign-off).</summary>
public sealed record AcknowledgePipCommand(AcknowledgePipInput Input) : IRequest<Result<PipDto>>;

public sealed class AcknowledgePipCommandHandler : IRequestHandler<AcknowledgePipCommand, Result<PipDto>>
{
    private readonly IPipService _service;
    public AcknowledgePipCommandHandler(IPipService service) => _service = service;

    public Task<Result<PipDto>> Handle(AcknowledgePipCommand request, CancellationToken cancellationToken)
        => _service.AcknowledgeAsync(request.Input, cancellationToken);
}

// ── Record checkpoint (AC-3/FR-4, manager OR HR) ────────────────────────

/// <summary>Records an append-only PIP checkpoint assessment (US-PRF-008 AC-3/FR-4).</summary>
public sealed record RecordPipCheckpointCommand(RecordCheckpointInput Input) : IRequest<Result<PipDto>>;

public sealed class RecordPipCheckpointCommandHandler : IRequestHandler<RecordPipCheckpointCommand, Result<PipDto>>
{
    private readonly IPipService _service;
    public RecordPipCheckpointCommandHandler(IPipService service) => _service = service;

    public Task<Result<PipDto>> Handle(RecordPipCheckpointCommand request, CancellationToken cancellationToken)
        => _service.RecordCheckpointAsync(request.Input, cancellationToken);
}

// ── Set final outcome (AC-4, HR-only BR-1) ──────────────────────────────

/// <summary>Sets the final PIP outcome — completed / extended / not-met (US-PRF-008 AC-4). HR-only (BR-1).</summary>
public sealed record SetPipOutcomeCommand(SetPipOutcomeInput Input) : IRequest<Result<PipDto>>;

public sealed class SetPipOutcomeCommandHandler : IRequestHandler<SetPipOutcomeCommand, Result<PipDto>>
{
    private readonly IPipService _service;
    public SetPipOutcomeCommandHandler(IPipService service) => _service = service;

    public Task<Result<PipDto>> Handle(SetPipOutcomeCommand request, CancellationToken cancellationToken)
        => _service.SetOutcomeAsync(request.Input, cancellationToken);
}

// ── Confirm escalation (AC-5/BR-6, HR-only) ─────────────────────────────

/// <summary>Confirms the escalation decision for a Not-Met PIP (US-PRF-008 AC-5/BR-6). HR-only.</summary>
public sealed record ConfirmPipEscalationCommand(ConfirmEscalationInput Input) : IRequest<Result<PipDto>>;

public sealed class ConfirmPipEscalationCommandHandler : IRequestHandler<ConfirmPipEscalationCommand, Result<PipDto>>
{
    private readonly IPipService _service;
    public ConfirmPipEscalationCommandHandler(IPipService service) => _service = service;

    public Task<Result<PipDto>> Handle(ConfirmPipEscalationCommand request, CancellationToken cancellationToken)
        => _service.ConfirmEscalationAsync(request.Input, cancellationToken);
}
