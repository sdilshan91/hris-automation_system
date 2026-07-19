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

// ── Bulk save (full-replace) ───────────────────────────────────────────────

/// <summary>
/// Bulk full-replace of an employee's goals for a cycle (US-PRF-001, BUG-243). The <see cref="Goals"/>
/// list is the complete desired set: items with an <c>Id</c> are updated, items without are created as
/// Draft, and any existing goal absent from the set is soft-deleted — all in one atomic SaveChanges.
/// </summary>
public sealed record SaveGoalsCommand(
    Guid EmployeeId,
    Guid CycleId,
    IReadOnlyList<SaveGoalItem> Goals
) : IRequest<Result<EmployeeGoalsDto>>;

public sealed class SaveGoalsCommandHandler : IRequestHandler<SaveGoalsCommand, Result<EmployeeGoalsDto>>
{
    private readonly IGoalService _service;
    public SaveGoalsCommandHandler(IGoalService service) => _service = service;

    public Task<Result<EmployeeGoalsDto>> Handle(SaveGoalsCommand request, CancellationToken cancellationToken)
        => _service.SaveGoalsAsync(request.EmployeeId, request.CycleId, request.Goals, cancellationToken);
}

// ── Finalize / lock (BUG-056) ─────────────────────────────────────────────

/// <summary>
/// BUG-056: finalizes (locks) an employee's goal set for a cycle after sign-off. The set's weights must
/// sum to exactly 100% (else 422 <c>weight_not_100</c>); an already-locked set yields 409
/// <c>goals_finalized</c>. On success every goal transitions to <c>Finalized</c> and further edits are
/// rejected until the set is re-opened (re-open flow out of scope).
/// </summary>
public sealed record FinalizeGoalsCommand(Guid EmployeeId, Guid CycleId)
    : IRequest<Result<EmployeeGoalsDto>>;

public sealed class FinalizeGoalsCommandHandler
    : IRequestHandler<FinalizeGoalsCommand, Result<EmployeeGoalsDto>>
{
    private readonly IGoalService _service;
    public FinalizeGoalsCommandHandler(IGoalService service) => _service = service;

    public Task<Result<EmployeeGoalsDto>> Handle(FinalizeGoalsCommand request, CancellationToken cancellationToken)
        => _service.FinalizeGoalsAsync(request.EmployeeId, request.CycleId, cancellationToken);
}

// ── Re-open / unlock (DF-46, BUG-056 follow-up) ────────────────────────────

/// <summary>
/// DF-46: re-opens (unlocks) a finalized goal set for a cycle. Requires HR (SetGoal.All) or the employee's
/// direct manager (SetGoal.Team) per BR-4 — same authz as finalize. A mandatory <see cref="Reason"/> is
/// recorded in the audit trail. A set that is not finalized yields 409 <c>goals_not_finalized</c>. On
/// success every finalized goal transitions back to <c>Acknowledged</c>, restoring writability.
/// </summary>
public sealed record ReopenGoalsCommand(Guid EmployeeId, Guid CycleId, string Reason)
    : IRequest<Result<EmployeeGoalsDto>>;

public sealed class ReopenGoalsCommandHandler
    : IRequestHandler<ReopenGoalsCommand, Result<EmployeeGoalsDto>>
{
    private readonly IGoalService _service;
    public ReopenGoalsCommandHandler(IGoalService service) => _service = service;

    public Task<Result<EmployeeGoalsDto>> Handle(ReopenGoalsCommand request, CancellationToken cancellationToken)
        => _service.ReopenGoalsAsync(request.EmployeeId, request.CycleId, request.Reason, cancellationToken);
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
