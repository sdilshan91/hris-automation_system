using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Save meeting notes (AC-1) ───────────────────────────────────────────

/// <summary>Saves/updates meeting notes without requesting sign-off (US-PRF-006 AC-1/BR-1).</summary>
public sealed record SaveMeetingNotesCommand(SaveMeetingNotesInput Input)
    : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class SaveMeetingNotesCommandHandler
    : IRequestHandler<SaveMeetingNotesCommand, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public SaveMeetingNotesCommandHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(SaveMeetingNotesCommand request, CancellationToken cancellationToken)
        => _service.SaveNotesAsync(request.Input, cancellationToken);
}

// ── Request employee sign-off (AC-2) ────────────────────────────────────

/// <summary>Saves notes and requests employee sign-off (US-PRF-006 AC-2).</summary>
public sealed record RequestSignOffCommand(SaveMeetingNotesInput Input, string? ClientIpAddress)
    : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class RequestSignOffCommandHandler
    : IRequestHandler<RequestSignOffCommand, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public RequestSignOffCommandHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(RequestSignOffCommand request, CancellationToken cancellationToken)
        => _service.RequestSignOffAsync(request.Input, request.ClientIpAddress, cancellationToken);
}

// ── Employee acknowledge & sign (AC-3) ──────────────────────────────────

/// <summary>Employee acknowledges &amp; signs the review (US-PRF-006 AC-3/FR-4 — locks the review).</summary>
public sealed record AcknowledgeReviewCommand(SignoffActionInput Input)
    : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class AcknowledgeReviewCommandHandler
    : IRequestHandler<AcknowledgeReviewCommand, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public AcknowledgeReviewCommandHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(AcknowledgeReviewCommand request, CancellationToken cancellationToken)
        => _service.AcknowledgeAsync(request.Input, cancellationToken);
}

// ── Employee dispute (AC-3/FR-5) ────────────────────────────────────────

/// <summary>Employee disputes the review with mandatory comments (US-PRF-006 AC-3/FR-4/FR-5).</summary>
public sealed record DisputeReviewCommand(SignoffActionInput Input)
    : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class DisputeReviewCommandHandler
    : IRequestHandler<DisputeReviewCommand, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public DisputeReviewCommandHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(DisputeReviewCommand request, CancellationToken cancellationToken)
        => _service.DisputeAsync(request.Input, cancellationToken);
}

// ── Caller-scoped self acknowledge & sign (AC-3, ISSUE-288) ─────────────

/// <summary>The caller acknowledges &amp; signs their OWN review for the active cycle (US-PRF-006 AC-3, ISSUE-288).</summary>
public sealed record AcknowledgeMyReviewCommand(string? ClientIpAddress)
    : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class AcknowledgeMyReviewCommandHandler
    : IRequestHandler<AcknowledgeMyReviewCommand, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public AcknowledgeMyReviewCommandHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(AcknowledgeMyReviewCommand request, CancellationToken cancellationToken)
        => _service.AcknowledgeMyReviewAsync(request.ClientIpAddress, cancellationToken);
}

// ── Caller-scoped self dispute (AC-3/FR-5, ISSUE-288) ───────────────────

/// <summary>The caller disputes their OWN review for the active cycle with mandatory comments (US-PRF-006 AC-3, ISSUE-288).</summary>
public sealed record DisputeMyReviewCommand(string Comments, string? ClientIpAddress)
    : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class DisputeMyReviewCommandHandler
    : IRequestHandler<DisputeMyReviewCommand, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public DisputeMyReviewCommandHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(DisputeMyReviewCommand request, CancellationToken cancellationToken)
        => _service.DisputeMyReviewAsync(request.Comments, request.ClientIpAddress, cancellationToken);
}

// ── HR resolve dispute (BR-4) ───────────────────────────────────────────

/// <summary>HR resolves a disputed review by confirming or amending (US-PRF-006 BR-4).</summary>
public sealed record ResolveDisputeCommand(ResolveDisputeInput Input)
    : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class ResolveDisputeCommandHandler
    : IRequestHandler<ResolveDisputeCommand, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public ResolveDisputeCommandHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(ResolveDisputeCommand request, CancellationToken cancellationToken)
        => _service.ResolveDisputeAsync(request.Input, cancellationToken);
}
