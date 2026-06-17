using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>Input for a single ad-hoc onboarding task on assign/modify (US-ONB-002 FR-5).</summary>
public sealed record AdHocTaskInput(
    string Title,
    string? Description,
    string? Category,
    OnboardingResponsibleRole ResponsibleRole,
    Guid? ResponsibleUserId,
    int DueOffsetDays,
    bool IsMandatory,
    int SortOrder);

/// <summary>Input for assigning an onboarding checklist to a new hire (US-ONB-002 AC-2).</summary>
public sealed record AssignChecklistInput(
    Guid EmployeeId,
    Guid TemplateId,
    DateTime? OverrideStartDate,
    ChecklistAssignmentMode Mode,
    IReadOnlyList<AdHocTaskInput> AdditionalTasks,
    string? IdempotencyKey);

/// <summary>A single task modification op (US-ONB-002 AC-4/FR-5/FR-6).</summary>
public sealed record ModifyTaskInput(Guid TaskInstanceId, DateTime? NewDueDate, bool Remove);

/// <summary>Input for modifying an assigned checklist (US-ONB-002 AC-4).</summary>
public sealed record ModifyChecklistInput(
    Guid ChecklistInstanceId,
    IReadOnlyList<AdHocTaskInput> AddTasks,
    IReadOnlyList<ModifyTaskInput> TaskChanges);

/// <summary>
/// Onboarding checklist assignment service (US-ONB-002). All operations are tenant-scoped via
/// ITenantContext + the EF global query filter (NFR-2). The tenant_id is taken from the session context,
/// never from user input (FR-7). Notification dispatch uses the outbox pattern (NFR-3): intent rows are
/// written in the assignment transaction and a Hangfire worker delivers them via INotificationDispatcher.
/// </summary>
public interface IOnboardingChecklistService
{
    /// <summary>AC-1/FR-1: active templates applicable to an employee (by dept + job title + universal).</summary>
    Task<Result<IReadOnlyList<ApplicableTemplateDto>>> GetApplicableTemplatesAsync(
        Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>AC-2/AC-3: assign a checklist (creates instance + task instances + outbox; replace/merge).</summary>
    Task<Result<OnboardingChecklistInstanceDto>> AssignAsync(
        AssignChecklistInput input, CancellationToken cancellationToken = default);

    /// <summary>Gets an assigned checklist instance (with its task instances), tenant-scoped.</summary>
    Task<Result<OnboardingChecklistInstanceDto>> GetInstanceAsync(
        Guid checklistInstanceId, CancellationToken cancellationToken = default);

    /// <summary>AC-4/FR-5/FR-6: add ad-hoc tasks, change due dates, soft-delete non-mandatory tasks (BR-3).</summary>
    Task<Result<OnboardingChecklistInstanceDto>> ModifyAsync(
        ModifyChecklistInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hangfire worker seam for dispatching pending onboarding notification-outbox rows (US-ONB-002 NFR-3).
/// Implemented in HRM.Api/Jobs so it can be enqueued via IBackgroundJobClient.
/// </summary>
public interface IOnboardingNotificationDispatchJob
{
    /// <summary>Reads pending outbox rows for the tenant and dispatches each via INotificationDispatcher.</summary>
    Task RunAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
