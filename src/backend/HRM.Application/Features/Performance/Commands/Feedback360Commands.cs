using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Add reviewer (AC-1/FR-2) ───────────────────────────────────────────────

/// <summary>Manually adds a 360 reviewer for an employee in a category (US-PRF-005 AC-1/FR-2).</summary>
public sealed record AddReviewerCommand(
    Guid CycleId, Guid RevieweeEmployeeId, Guid ReviewerEmployeeId, ReviewerCategory Category
) : IRequest<Result<ReviewerAssignmentDto>>;

public sealed class AddReviewerCommandHandler : IRequestHandler<AddReviewerCommand, Result<ReviewerAssignmentDto>>
{
    private readonly IReviewerAssignmentService _service;
    public AddReviewerCommandHandler(IReviewerAssignmentService service) => _service = service;

    public Task<Result<ReviewerAssignmentDto>> Handle(AddReviewerCommand request, CancellationToken cancellationToken)
        => _service.AddReviewerAsync(
            request.RevieweeEmployeeId, request.CycleId, request.ReviewerEmployeeId, request.Category, cancellationToken);
}

// ── Remove reviewer (AC-1/FR-2) ────────────────────────────────────────────

/// <summary>Removes a 360 reviewer assignment (US-PRF-005 AC-1/FR-2).</summary>
public sealed record RemoveReviewerCommand(Guid AssignmentId) : IRequest<Result>;

public sealed class RemoveReviewerCommandHandler : IRequestHandler<RemoveReviewerCommand, Result>
{
    private readonly IReviewerAssignmentService _service;
    public RemoveReviewerCommandHandler(IReviewerAssignmentService service) => _service = service;

    public Task<Result> Handle(RemoveReviewerCommand request, CancellationToken cancellationToken)
        => _service.RemoveReviewerAsync(request.AssignmentId, cancellationToken);
}

// ── Full-replace reviewer set (BUG-244 #1, AC-1/FR-2) ──────────────────────

/// <summary>
/// BUG-244 #1: full-replace of an employee's manual (Peer + Direct Report) 360 reviewers in the active
/// 360-enabled cycle (US-PRF-005 AC-1/FR-2). The reviewee rides the route; the cycle is resolved server-side.
/// </summary>
public sealed record SaveReviewersCommand(
    Guid RevieweeEmployeeId, IReadOnlyList<ReviewerInputDto> Reviewers
) : IRequest<Result<ReviewerConfigurationDto>>;

public sealed class SaveReviewersCommandHandler : IRequestHandler<SaveReviewersCommand, Result<ReviewerConfigurationDto>>
{
    private readonly IReviewerAssignmentService _service;
    public SaveReviewersCommandHandler(IReviewerAssignmentService service) => _service = service;

    public Task<Result<ReviewerConfigurationDto>> Handle(SaveReviewersCommand request, CancellationToken cancellationToken)
        => _service.SaveReviewersAsync(request.RevieweeEmployeeId, request.Reviewers, cancellationToken);
}

// ── Notify reviewers — enter 360 phase (AC-2) ──────────────────────────────

/// <summary>Notifies all assigned reviewers that the 360 phase has started (US-PRF-005 AC-2).</summary>
public sealed record NotifyReviewersCommand(Guid CycleId, Guid RevieweeEmployeeId) : IRequest<Result<int>>;

public sealed class NotifyReviewersCommandHandler : IRequestHandler<NotifyReviewersCommand, Result<int>>
{
    private readonly IReviewerAssignmentService _service;
    public NotifyReviewersCommandHandler(IReviewerAssignmentService service) => _service = service;

    public Task<Result<int>> Handle(NotifyReviewersCommand request, CancellationToken cancellationToken)
        => _service.NotifyReviewersAsync(request.RevieweeEmployeeId, request.CycleId, cancellationToken);
}

// ── Submit feedback (AC-3) ─────────────────────────────────────────────────

/// <summary>Submits the calling reviewer's 360 feedback for an employee (US-PRF-005 AC-3).</summary>
public sealed record SubmitFeedback360Command(
    Guid CycleId, Guid RevieweeEmployeeId, string? OverallComment, IReadOnlyList<Feedback360ItemInput> Items
) : IRequest<Result<Feedback360ResultEntryDto>>;

public sealed class SubmitFeedback360CommandHandler
    : IRequestHandler<SubmitFeedback360Command, Result<Feedback360ResultEntryDto>>
{
    private readonly IFeedback360Service _service;
    public SubmitFeedback360CommandHandler(IFeedback360Service service) => _service = service;

    public Task<Result<Feedback360ResultEntryDto>> Handle(SubmitFeedback360Command request, CancellationToken cancellationToken)
        => _service.SubmitFeedbackAsync(
            new SubmitFeedback360Input(request.CycleId, request.RevieweeEmployeeId, request.OverallComment, request.Items),
            cancellationToken);
}

// ── Release results to the reviewee (BR-4/FR-3) ────────────────────────────

/// <summary>
/// Releases a reviewee's aggregated 360 results to them for a cycle (US-PRF-005 BR-4/FR-3). HR or the reviewee's
/// own reporting manager; hard-blocked below the cycle's minimum peer threshold; idempotent.
/// </summary>
public sealed record ReleaseFeedback360Command(Guid CycleId, Guid RevieweeEmployeeId)
    : IRequest<Result<Feedback360ReleaseDto>>;

public sealed class ReleaseFeedback360CommandHandler
    : IRequestHandler<ReleaseFeedback360Command, Result<Feedback360ReleaseDto>>
{
    private readonly IFeedback360Service _service;
    public ReleaseFeedback360CommandHandler(IFeedback360Service service) => _service = service;

    public Task<Result<Feedback360ReleaseDto>> Handle(ReleaseFeedback360Command request, CancellationToken cancellationToken)
        => _service.ReleaseResultsAsync(request.CycleId, request.RevieweeEmployeeId, cancellationToken);
}

// ── Submit feedback by assignment (BUG-244, reviewer form submit) ──────────

/// <summary>
/// BUG-244: submits the reviewer form keyed by the assignment id (US-PRF-005 AC-3). The cycle + reviewee +
/// reviewer are resolved from the assignment; RLS is enforced in the service. Returns the now-locked form.
/// </summary>
public sealed record SubmitFeedbackByAssignmentCommand(
    Guid AssignmentId, IReadOnlyList<FeedbackAnswerInput> Answers
) : IRequest<Result<FeedbackFormDto>>;

public sealed class SubmitFeedbackByAssignmentCommandHandler
    : IRequestHandler<SubmitFeedbackByAssignmentCommand, Result<FeedbackFormDto>>
{
    private readonly IFeedback360Service _service;
    public SubmitFeedbackByAssignmentCommandHandler(IFeedback360Service service) => _service = service;

    public Task<Result<FeedbackFormDto>> Handle(SubmitFeedbackByAssignmentCommand request, CancellationToken cancellationToken)
        => _service.SubmitFeedbackByAssignmentAsync(request.AssignmentId, request.Answers, cancellationToken);
}
