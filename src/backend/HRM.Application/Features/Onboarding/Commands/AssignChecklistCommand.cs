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
/// <param name="ResolvedTasks">
/// BUG-441 replace mode. <c>null</c> = legacy (expand the template, then append
/// <paramref name="AdditionalTasks"/>); non-null = the authoritative task set, created verbatim with the
/// template NOT expanded. Trailing + defaulted so existing constructions keep compiling.
/// </param>
public sealed record AssignChecklistCommand(
    Guid EmployeeId,
    Guid TemplateId,
    DateOnly? OverrideStartDate,
    ChecklistAssignmentMode Mode,
    IReadOnlyList<AssignChecklistAdHocTask> AdditionalTasks,
    string? IdempotencyKey,
    IReadOnlyList<AssignChecklistResolvedTask>? ResolvedTasks = null
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

/// <summary>
/// BUG-441: one line of the authoritative task set (replace mode). <c>DueDate</c> is nullable here — and
/// only here — so a missing date fails validation with "due date is required" instead of binding to
/// <c>default(DateOnly)</c> and quietly assigning 0001-01-01.
/// </summary>
public sealed record AssignChecklistResolvedTask(
    Guid? TemplateTaskId,
    string Title,
    string? Description,
    string? Category,
    Domain.Enums.OnboardingResponsibleRole? ResponsibleRole,
    DateOnly? DueDate,
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
            request.IdempotencyKey,
            // Null stays null (legacy expansion); an empty list stays an empty list (an authoritative
            // "no tasks"). The validator has already guaranteed every DueDate is present.
            request.ResolvedTasks?.Select(t => new ResolvedTaskInput(
                t.TemplateTaskId, t.Title, t.Description, t.Category, t.ResponsibleRole,
                t.DueDate!.Value, t.IsMandatory, t.SortOrder)).ToList()),
            cancellationToken);
}
