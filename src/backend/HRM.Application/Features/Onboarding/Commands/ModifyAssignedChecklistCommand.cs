using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>
/// Modifies an assigned onboarding checklist (US-ONB-002 AC-4/FR-5/FR-6): adds ad-hoc tasks (due date =
/// today + offset), changes due dates on existing tasks, and soft-deletes non-mandatory tasks. Mandatory
/// tasks cannot be removed (BR-3).
/// </summary>
public sealed record ModifyAssignedChecklistCommand(
    Guid ChecklistInstanceId,
    IReadOnlyList<AssignChecklistAdHocTask> AddTasks,
    IReadOnlyList<ModifyChecklistTaskChange> TaskChanges
) : IRequest<Result<OnboardingChecklistInstanceDto>>;

/// <summary>A single task modification op on the modify command (US-ONB-002 AC-4/FR-6).</summary>
public sealed record ModifyChecklistTaskChange(Guid TaskInstanceId, DateTime? NewDueDate, bool Remove);

public sealed class ModifyAssignedChecklistCommandHandler
    : IRequestHandler<ModifyAssignedChecklistCommand, Result<OnboardingChecklistInstanceDto>>
{
    private readonly IOnboardingChecklistService _service;

    public ModifyAssignedChecklistCommandHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<OnboardingChecklistInstanceDto>> Handle(
        ModifyAssignedChecklistCommand request, CancellationToken cancellationToken)
        => _service.ModifyAsync(new ModifyChecklistInput(
            request.ChecklistInstanceId,
            request.AddTasks.Select(t => new AdHocTaskInput(
                t.Title, t.Description, t.Category, t.ResponsibleRole, t.ResponsibleUserId,
                t.DueOffsetDays, t.IsMandatory, t.SortOrder)).ToList(),
            request.TaskChanges.Select(c => new ModifyTaskInput(c.TaskInstanceId, c.NewDueDate, c.Remove)).ToList()),
            cancellationToken);
}
