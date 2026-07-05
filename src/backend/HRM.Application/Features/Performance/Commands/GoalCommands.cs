using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Create ───────────────────────────────────────────────────────────────

/// <summary>Creates a goal for a direct report during the goal-setting window (US-PRF-001 FR-1/AC-2).</summary>
public sealed record CreateGoalCommand(
    Guid CycleId,
    Guid EmployeeId,
    string Title,
    string? Description,
    GoalCategory Category,
    int Weight,
    string TargetValue,
    string MeasurementUnit,
    DateOnly DueDate,
    Guid? ParentGoalId
) : IRequest<Result<GoalDto>>;

public sealed class CreateGoalCommandHandler : IRequestHandler<CreateGoalCommand, Result<GoalDto>>
{
    private readonly IGoalService _service;
    public CreateGoalCommandHandler(IGoalService service) => _service = service;

    public Task<Result<GoalDto>> Handle(CreateGoalCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new GoalInput(
            request.CycleId, request.EmployeeId, request.Title, request.Description, request.Category,
            request.Weight, request.TargetValue, request.MeasurementUnit, request.DueDate,
            request.ParentGoalId), cancellationToken);
}

// ── Update ───────────────────────────────────────────────────────────────

/// <summary>
/// Updates an existing goal (US-PRF-001 FR-1). Cycle + employee are NOT in the command — they are
/// immutable for a goal and resolved from the persisted row by the service.
/// </summary>
public sealed record UpdateGoalCommand(
    Guid GoalId,
    string Title,
    string? Description,
    GoalCategory Category,
    int Weight,
    string TargetValue,
    string MeasurementUnit,
    DateOnly DueDate,
    Guid? ParentGoalId,
    // BUG-057: optimistic concurrency token the client read; round-trips to guard the UPDATE.
    uint RowVersion = 0
) : IRequest<Result<GoalDto>>;

public sealed class UpdateGoalCommandHandler : IRequestHandler<UpdateGoalCommand, Result<GoalDto>>
{
    private readonly IGoalService _service;
    public UpdateGoalCommandHandler(IGoalService service) => _service = service;

    public Task<Result<GoalDto>> Handle(UpdateGoalCommand request, CancellationToken cancellationToken)
        // CycleId/EmployeeId in GoalInput are ignored on update (the service keeps the goal's own values).
        => _service.UpdateAsync(request.GoalId, new GoalInput(
            Guid.Empty, Guid.Empty, request.Title, request.Description, request.Category,
            request.Weight, request.TargetValue, request.MeasurementUnit, request.DueDate,
            request.ParentGoalId, request.RowVersion), cancellationToken);
}

// ── Delete ───────────────────────────────────────────────────────────────

/// <summary>Soft-deletes a goal (US-PRF-001 FR-1).</summary>
public sealed record DeleteGoalCommand(Guid GoalId) : IRequest<Result>;

public sealed class DeleteGoalCommandHandler : IRequestHandler<DeleteGoalCommand, Result>
{
    private readonly IGoalService _service;
    public DeleteGoalCommandHandler(IGoalService service) => _service = service;

    public Task<Result> Handle(DeleteGoalCommand request, CancellationToken cancellationToken)
        => _service.DeleteAsync(request.GoalId, cancellationToken);
}
