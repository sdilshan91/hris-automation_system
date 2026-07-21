using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>
/// Assigns an onboarding checklist to a new hire (US-ONB-002 AC-2/AC-3). Creates a checklist instance with
/// task instances (due_date = start_date + due_offset_days, status pending), resolves responsible parties
/// (FR-3), writes notification-outbox rows in the same transaction (NFR-3) and enqueues the Hangfire
/// dispatch worker. The assignment mode handles the duplicate case (replace / merge, AC-3).
/// </summary>
public sealed record AssignChecklistCommand(
    Guid EmployeeId,
    Guid TemplateId,
    DateOnly? OverrideStartDate,
    ChecklistAssignmentMode Mode,
    IReadOnlyList<AssignChecklistAdHocTask> AdditionalTasks,
    string? IdempotencyKey
) : IRequest<Result<OnboardingChecklistInstanceDto>>;

/// <summary>An ad-hoc task line on the assign command (US-ONB-002 FR-5).</summary>
public sealed record AssignChecklistAdHocTask(
    string Title,
    string? Description,
    string? Category,
    Domain.Enums.OnboardingResponsibleRole ResponsibleRole,
    Guid? ResponsibleUserId,
    int DueOffsetDays,
    bool IsMandatory,
    int SortOrder);

public sealed class AssignChecklistCommandHandler
    : IRequestHandler<AssignChecklistCommand, Result<OnboardingChecklistInstanceDto>>
{
    private readonly IOnboardingChecklistService _service;

    public AssignChecklistCommandHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<OnboardingChecklistInstanceDto>> Handle(
        AssignChecklistCommand request, CancellationToken cancellationToken)
        => _service.AssignAsync(new AssignChecklistInput(
            request.EmployeeId,
            request.TemplateId,
            request.OverrideStartDate,
            request.Mode,
            request.AdditionalTasks.Select(t => new AdHocTaskInput(
                t.Title, t.Description, t.Category, t.ResponsibleRole, t.ResponsibleUserId,
                t.DueOffsetDays, t.IsMandatory, t.SortOrder)).ToList(),
            request.IdempotencyKey),
            cancellationToken);
}
