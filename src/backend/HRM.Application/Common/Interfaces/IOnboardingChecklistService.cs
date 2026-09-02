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

/// <summary>
/// BUG-441: one line of the authoritative task set on a replace-mode assignment. Carries a CONCRETE
/// <paramref name="DueDate"/> (FR-6 inline edits) rather than an offset, and no responsible-user id — the
/// service re-resolves ownership from the role (FR-3).
/// </summary>
public sealed record ResolvedTaskInput(
    Guid? TemplateTaskId,
    string Title,
    string? Description,
    string? Category,
    OnboardingResponsibleRole? ResponsibleRole,
    DateOnly DueDate,
    bool IsMandatory,
    int SortOrder);

/// <summary>
/// Input for assigning an onboarding checklist to a new hire (US-ONB-002 AC-2).
/// </summary>
/// <param name="AdditionalTasks">
/// Legacy FR-5 extras, appended ON TOP of the expanded template.
/// </param>
/// <param name="ResolvedTasks">
/// BUG-441. <c>null</c> = legacy: expand the template, then append <paramref name="AdditionalTasks"/>.
/// Non-null = replace mode: create exactly these tasks and do NOT expand the template. Trailing + defaulted
/// so existing callers (e.g. the hire-conversion path) are untouched.
/// </param>
public sealed record AssignChecklistInput(
    Guid EmployeeId,
    Guid TemplateId,
    DateOnly? OverrideStartDate,
    ChecklistAssignmentMode Mode,
    IReadOnlyList<AdHocTaskInput> AdditionalTasks,
    string? IdempotencyKey,
    IReadOnlyList<ResolvedTaskInput>? ResolvedTasks = null);

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

    /// <summary>
    /// FR-2/BR-4: resolves what <paramref name="templateId"/> WOULD assign to <paramref name="employeeId"/> —
    /// the task list, the anchored due dates and the resolved responsible parties — and persists NOTHING.
    ///
    /// <para>This is a pure read. It reuses the same start-date anchoring, responsible-party resolution and
    /// task-construction helpers as <see cref="AssignAsync"/>, so preview and assign cannot disagree, but it
    /// never calls SaveChanges, never adds an entity to the context, and never touches the assign path's
    /// idempotency handling. The precedent is AttendancePolicyResolver, which deliberately never lazily
    /// creates a policy row because a payroll run must not write policy as a side effect.</para>
    ///
    /// <para>Validation mirrors assign: 404 when the employee or template is not found in this tenant,
    /// 409 when the template is inactive (BR-1) — so the preview cannot show a template that assign would
    /// then reject.</para>
    /// </summary>
    Task<Result<ChecklistPreviewDto>> PreviewAsync(
        Guid employeeId, Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>AC-2/AC-3: assign a checklist (creates instance + task instances + outbox; replace/merge).</summary>
    Task<Result<OnboardingChecklistInstanceDto>> AssignAsync(
        AssignChecklistInput input, CancellationToken cancellationToken = default);

    /// <summary>Gets an assigned checklist instance (with its task instances), tenant-scoped.</summary>
    /// <summary>
    /// GAP-013 / US-ONB-002 AC-3: the employee's CURRENT ACTIVE checklist, or null when they have none.
    ///
    /// <para>This is what drives the replace/merge prompt: before assigning, the UI has to know whether the
    /// employee already has an active checklist. The frontend has always called
    /// <c>GET /onboarding/checklists/employee/{employeeId}</c> for it and the route did not exist, so
    /// <c>canAssign</c> could never resolve and AC-3 was unreachable.</para>
    ///
    /// <para>Returns <c>Success(null)</c> — NOT a 404 — when the employee has no active checklist: "no
    /// checklist" is the normal case for a first assignment, and a 404 would make the FE treat an expected
    /// state as an error.</para>
    /// </summary>
    Task<Result<OnboardingChecklistInstanceDto?>> GetActiveByEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// GAP-027: streams a task's attachment from an authenticated endpoint.
    /// </summary>
    /// <remarks>
    /// The DTO used to carry a <c>/files/{tenantId}/{path}</c> URL, rendered by my-checklist as
    /// <c>&lt;a [href]&gt;</c>. No route served it, so the link opened a 404 in a new tab.
    /// </remarks>
    Task<Result<StoredFileResult>> DownloadTaskAttachmentAsync(
        Guid taskInstanceId, CancellationToken cancellationToken = default);

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
