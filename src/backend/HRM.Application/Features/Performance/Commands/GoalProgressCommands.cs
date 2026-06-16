using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Add progress update (AC-2/FR-1/FR-2) ────────────────────────────────

/// <summary>Posts an append-only goal progress update (US-PRF-009 AC-2/FR-1/FR-2). Employee-only, own goal.</summary>
public sealed record AddGoalProgressCommand(AddGoalProgressInput Input) : IRequest<Result<GoalTimelineDto>>;

public sealed class AddGoalProgressCommandHandler : IRequestHandler<AddGoalProgressCommand, Result<GoalTimelineDto>>
{
    private readonly IGoalProgressService _service;
    public AddGoalProgressCommandHandler(IGoalProgressService service) => _service = service;

    public Task<Result<GoalTimelineDto>> Handle(AddGoalProgressCommand request, CancellationToken cancellationToken)
        => _service.AddProgressUpdateAsync(request.Input, cancellationToken);
}

// ── Manager comment (FR-8) ──────────────────────────────────────────────

/// <summary>Adds a manager/HR comment to a goal's thread (US-PRF-009 FR-8).</summary>
public sealed record AddGoalCommentCommand(AddGoalCommentInput Input) : IRequest<Result<GoalTimelineDto>>;

public sealed class AddGoalCommentCommandHandler : IRequestHandler<AddGoalCommentCommand, Result<GoalTimelineDto>>
{
    private readonly IGoalProgressService _service;
    public AddGoalCommentCommandHandler(IGoalProgressService service) => _service = service;

    public Task<Result<GoalTimelineDto>> Handle(AddGoalCommentCommand request, CancellationToken cancellationToken)
        => _service.AddCommentAsync(request.Input, cancellationToken);
}
