using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Save as draft (AC-3/FR-6) ──────────────────────────────────────────

/// <summary>Persists partial self-assessment progress without submitting (US-PRF-002 AC-3/FR-6).</summary>
public sealed record SaveSelfAssessmentDraftCommand(
    Guid CycleId,
    IReadOnlyList<SelfAssessmentItemInput> Items
) : IRequest<Result<SelfAssessmentDto>>;

public sealed class SaveSelfAssessmentDraftCommandHandler
    : IRequestHandler<SaveSelfAssessmentDraftCommand, Result<SelfAssessmentDto>>
{
    private readonly ISelfAssessmentService _service;
    public SaveSelfAssessmentDraftCommandHandler(ISelfAssessmentService service) => _service = service;

    public Task<Result<SelfAssessmentDto>> Handle(SaveSelfAssessmentDraftCommand request, CancellationToken cancellationToken)
        => _service.SaveDraftAsync(new SaveSelfAssessmentInput(request.CycleId, request.Items), cancellationToken);
}

// ── Submit (AC-2) ──────────────────────────────────────────────────────

/// <summary>Submits the self-assessment after all goals are rated (US-PRF-002 AC-2).</summary>
public sealed record SubmitSelfAssessmentCommand(
    Guid CycleId,
    IReadOnlyList<SelfAssessmentItemInput> Items
) : IRequest<Result<SelfAssessmentDto>>;

public sealed class SubmitSelfAssessmentCommandHandler
    : IRequestHandler<SubmitSelfAssessmentCommand, Result<SelfAssessmentDto>>
{
    private readonly ISelfAssessmentService _service;
    public SubmitSelfAssessmentCommandHandler(ISelfAssessmentService service) => _service = service;

    public Task<Result<SelfAssessmentDto>> Handle(SubmitSelfAssessmentCommand request, CancellationToken cancellationToken)
        => _service.SubmitAsync(new SaveSelfAssessmentInput(request.CycleId, request.Items), cancellationToken);
}

// ── Reopen (BR-3/AC-2, manager/HR only — BUG-059) ──────────────────────

/// <summary>
/// Reopens a submitted self-assessment back to Draft so the employee can edit and re-submit
/// (US-PRF-004 BR-3/AC-2 — BUG-059). Manager/HR-only; an employee can never self-reopen.
/// </summary>
public sealed record ReopenSelfAssessmentCommand(Guid EmployeeId, Guid CycleId, string? Reason)
    : IRequest<Result<SelfAssessmentDto>>;

public sealed class ReopenSelfAssessmentCommandHandler
    : IRequestHandler<ReopenSelfAssessmentCommand, Result<SelfAssessmentDto>>
{
    private readonly ISelfAssessmentService _service;
    public ReopenSelfAssessmentCommandHandler(ISelfAssessmentService service) => _service = service;

    public Task<Result<SelfAssessmentDto>> Handle(ReopenSelfAssessmentCommand request, CancellationToken cancellationToken)
        => _service.ReopenAsync(request.EmployeeId, request.CycleId, request.Reason, cancellationToken);
}
