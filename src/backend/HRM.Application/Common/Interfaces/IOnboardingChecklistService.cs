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
    DateOnly? OverrideStartDate,
    ChecklistAssignmentMode Mode,
    IReadOnlyList<AdHocTaskInput> AdditionalTasks,
    string? IdempotencyKey);

/// <summary>A single task modification op (US-ONB-002 AC-4/FR-5/FR-6).</summary>
public sealed record ModifyTaskInput(Guid TaskInstanceId, DateOnly? NewDueDate, bool Remove);

/// <summary>Input for modifying an assigned checklist (US-ONB-002 AC-4).</summary>
public sealed record ModifyChecklistInput(
    Guid ChecklistInstanceId,
    IReadOnlyList<AdHocTaskInput> AddTasks,
    IReadOnlyList<ModifyTaskInput> TaskChanges);

/// <summary>
/// Input for an employee completing one of their own onboarding tasks (US-ONB-003 AC-3/AC-4/FR-2/FR-3).
/// The attachment is optional and only supplied when the task requires a document. The file stream (when
/// present) is owned by the caller; the service reads it before persistence.
/// </summary>
public sealed record CompleteTaskInput(
    Guid TaskInstanceId,
    string? Comment,
    Stream? AttachmentStream,
    string? AttachmentFileName,
    string? AttachmentContentType,
    long AttachmentSize);

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

    // ── US-ONB-003: self-service (the logged-in employee acts on their own tasks) ──

    /// <summary>
    /// US-ONB-003 AC-1/AC-2/FR-1: the current employee's active checklist with tasks grouped by category and
    /// a progress summary. Returns 404 when the caller is not linked to an employee or has no active checklist.
    /// </summary>
    Task<Result<MyChecklistDto>> GetMyChecklistAsync(CancellationToken cancellationToken = default);

    /// <summary>US-ONB-003 AC-1/FR-4: the cheap progress summary for the dashboard widget.</summary>
    Task<Result<MyChecklistProgressDto>> GetMyChecklistProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// US-ONB-003 AC-3/AC-4/FR-2/FR-3/FR-7/BR-1/BR-3: the current employee marks one of their OWN tasks
    /// (responsible_role = Employee) completed, optionally with a comment and a required document. Recalculates
    /// progress and writes notification-outbox rows to HR + manager.
    /// </summary>
    Task<Result<CompleteTaskResultDto>> CompleteTaskAsync(
        CompleteTaskInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-ONB-003 FR-6/AC-5/BR-4: the daily overdue sweep for a single tenant. Finds past-due, not-completed
    /// tasks and writes overdue notification-outbox rows to the employee, HR, and manager (idempotent per task
    /// per UTC day). Runs at system level (bypasses the query filter, scopes by tenant id). Returns the number
    /// of outbox rows written. NOTE: tenant-timezone scheduling is not built — the sweep uses UTC.
    /// </summary>
    Task<int> RunOverdueSweepAsync(Guid tenantId, CancellationToken cancellationToken = default);
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
