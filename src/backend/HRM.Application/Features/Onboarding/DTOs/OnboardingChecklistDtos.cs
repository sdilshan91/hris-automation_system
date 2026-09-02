using HRM.Domain.Enums;

namespace HRM.Application.Features.Onboarding.DTOs;

/// <summary>How to handle an assignment when the employee already has an active checklist (US-ONB-002 AC-3).</summary>
public enum ChecklistAssignmentMode
{
    /// <summary>Supersede the existing active checklist with a new version (BR-2).</summary>
    Replace = 0,

    /// <summary>Add the template's tasks onto the existing active checklist (AC-3 "add additional tasks").</summary>
    Merge = 1,
}

/// <summary>
/// Full assigned-checklist representation returned from assign / get / modify (US-ONB-002 §7 output).
/// </summary>
public sealed record OnboardingChecklistInstanceDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid TemplateId { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public OnboardingChecklistStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public int Version { get; init; }
    public Guid? AssignedByUserId { get; init; }
    public IReadOnlyList<OnboardingTaskInstanceDto> Tasks { get; init; } = [];
    public int TaskCount { get; init; }
    public int NotificationsQueued { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>A single assigned task instance (US-ONB-002 AC-2).</summary>
public sealed record OnboardingTaskInstanceDto
{
    public Guid Id { get; init; }
    public Guid? SourceTemplateTaskId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public OnboardingResponsibleRole ResponsibleRole { get; init; }
    public string ResponsibleRoleName { get; init; } = string.Empty;
    public Guid? ResponsibleUserId { get; init; }
    public DateOnly DueDate { get; init; }
    public OnboardingTaskStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public bool IsMandatory { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>An applicable template row for the assignment dropdown (US-ONB-002 AC-1/FR-1).</summary>
public sealed record ApplicableTemplateDto
{
    public Guid Id { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsUniversal { get; init; }
    public int TaskCount { get; init; }
    public int MandatoryTaskCount { get; init; }
}

// ── Request bodies ───────────────────────────────────────────────────

/// <summary>Request body for an ad-hoc task added during assignment or modification (FR-5).</summary>
public sealed record AdHocTaskRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public OnboardingResponsibleRole ResponsibleRole { get; init; } = OnboardingResponsibleRole.HR;
    public Guid? ResponsibleUserId { get; init; }
    public int DueOffsetDays { get; init; }
    public bool IsMandatory { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// BUG-441: one line of the AUTHORITATIVE task set on a replace-mode assignment (FR-6).
///
/// <para>Sent by the assignment screen after the HR officer has reviewed — and inline-edited — the
/// previewed task list. When <see cref="AssignChecklistRequest.ResolvedTasks"/> is present the server
/// creates exactly these tasks and does NOT expand the template again, which is what stops every template
/// task being created twice (once from <c>template.Tasks</c>, once from the echoed payload).</para>
///
/// <para><b>Carries a concrete <see cref="DueDate"/>, not an offset.</b> The whole point of FR-6 is that
/// the officer may move a single task's date; an offset would have to be re-derived against a start date
/// the officer may also have overridden, and the previous <c>additionalTasks</c> route lost every edit
/// precisely because it could only express <c>startDate + DueOffsetDays</c> and the client never sent one.
/// The preview returns a concrete date, so replace-mode echoes a concrete date back.</para>
///
/// <para><b>Ownership is NOT client-supplied.</b> There is deliberately no <c>responsibleUserId</c> on this
/// contract: the server re-resolves the responsible party from the role via FR-3 (the same resolution the
/// preview ran), so an edited role picks up the right owner and no caller can name an arbitrary user id.
/// Any <c>responsibleUserId</c>/<c>id</c>/<c>responsibleName</c> the UI happens to echo is ignored.</para>
/// </summary>
public sealed record ResolvedTaskRequest
{
    /// <summary>
    /// The template task this row came from, or null for a task the officer added by hand (FR-5). Must
    /// belong to the template being assigned — an unknown id is rejected, never silently treated as ad-hoc.
    /// </summary>
    public Guid? TemplateTaskId { get; init; }

    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }

    /// <summary>
    /// Nullable because the assignment form's role control is nullable on the wire. Null falls back to the
    /// source template task's role, then to <see cref="OnboardingResponsibleRole.HR"/>.
    /// </summary>
    public OnboardingResponsibleRole? ResponsibleRole { get; init; }

    /// <summary>The date this task is due (FR-6). Required — an omitted date is a 400, never "start + 0".</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// Honoured for ad-hoc rows only. For a template-derived row the template's own flag wins, so a
    /// mandatory task cannot be downgraded to optional on the way in (BR-3).
    /// </summary>
    public bool IsMandatory { get; init; }

    public int SortOrder { get; init; }
}

/// <summary>Request body for assigning a checklist to a new hire (US-ONB-002 §7 input).</summary>
public sealed record AssignChecklistRequest
{
    public Guid EmployeeId { get; init; }
    public Guid TemplateId { get; init; }
    public DateOnly? OverrideStartDate { get; init; }
    public ChecklistAssignmentMode Mode { get; init; } = ChecklistAssignmentMode.Replace;

    /// <summary>
    /// LEGACY (FR-5): extra tasks to append ON TOP of the template's own tasks. Unchanged behaviour — the
    /// template is still expanded and these are added after it. Ignored-by-rejection when
    /// <see cref="ResolvedTasks"/> is supplied (see that property).
    /// </summary>
    public IReadOnlyList<AdHocTaskRequest> AdditionalTasks { get; init; } = [];

    /// <summary>
    /// BUG-441 replace-mode: the complete, authoritative task set for this assignment.
    ///
    /// <para><b>Precedence.</b> <c>null</c> (absent) = legacy behaviour: expand the template, then append
    /// <see cref="AdditionalTasks"/>. Present = these rows are created verbatim and the template is NOT
    /// expanded. The two are mutually exclusive: supplying a non-empty <see cref="AdditionalTasks"/>
    /// alongside a present <c>ResolvedTasks</c> is a 400, not a silent drop — quietly discarding one of two
    /// task sets is the same class of invisible data loss this bug was.</para>
    ///
    /// <para>An empty array is meaningful and different from <c>null</c>: it means "assign a checklist with
    /// no tasks". Every mandatory template task must still be present in the set (BR-3).</para>
    /// </summary>
    public IReadOnlyList<ResolvedTaskRequest>? ResolvedTasks { get; init; }

    public string? IdempotencyKey { get; init; }
}

/// <summary>A single task modification op when editing an assigned checklist (US-ONB-002 AC-4/FR-5/FR-6).</summary>
public sealed record ModifyTaskRequest
{
    public Guid TaskInstanceId { get; init; }
    public DateOnly? NewDueDate { get; init; }
    public bool Remove { get; init; }
}

/// <summary>Request body for modifying an assigned checklist (US-ONB-002 AC-4).</summary>
public sealed record ModifyChecklistRequest
{
    public IReadOnlyList<AdHocTaskRequest> AddTasks { get; init; } = [];
    public IReadOnlyList<ModifyTaskRequest> TaskChanges { get; init; } = [];
}

// ── Preview (GET /onboarding/checklists/preview) ──────────────────────

/// <summary>
/// GAP: what a template WOULD create for an employee, resolved but NOT persisted (US-ONB-002 FR-2/BR-4).
/// The assignment screen renders this before the HR officer confirms, so it must be a pure read — no
/// checklist instance, no task instances, no outbox rows (same discipline as AttendancePolicyResolver,
/// which deliberately never lazily creates a policy row).
///
/// <para>Shape is pinned to the frontend's <c>IChecklistPreview</c>
/// (features/onboarding/models/onboarding-checklist.models.ts) — do not drift.</para>
/// </summary>
public sealed record ChecklistPreviewDto
{
    public Guid EmployeeId { get; init; }

    /// <summary>"First Last" of the new hire; null only when the employee has no name on record.</summary>
    public string? EmployeeName { get; init; }

    public Guid TemplateId { get; init; }
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>The date the offsets are measured from — joining date, or today when that is past (BR-4).</summary>
    public DateOnly StartDate { get; init; }

    public IReadOnlyList<ChecklistPreviewTaskDto> Tasks { get; init; } = [];
}

/// <summary>
/// One task the template would create, with its server-calculated due date (FR-2/BR-4) and resolved
/// responsible party (FR-3). Carries NO instance id: nothing exists yet (the FE contract marks
/// <c>IChecklistTask.id</c> as absent in a fresh preview).
/// </summary>
public sealed record ChecklistPreviewTaskDto
{
    /// <summary>The source template task this row came from (never null in a preview — all rows are template rows).</summary>
    public Guid? TemplateTaskId { get; init; }

    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public OnboardingResponsibleRole ResponsibleRole { get; init; }

    /// <summary>Resolved responsible user (FR-3) — null when the role resolves to nobody/many (BR-5).</summary>
    public Guid? ResponsibleUserId { get; init; }

    /// <summary>Display name for <see cref="ResponsibleUserId"/>, resolved tenant-scoped from Employees.</summary>
    public string? ResponsibleName { get; init; }

    /// <summary>The template's offset in days from <see cref="ChecklistPreviewDto.StartDate"/>.</summary>
    public int DueOffsetDays { get; init; }

    public DateOnly DueDate { get; init; }

    /// <summary>Always <c>pending</c> — a previewed task has no lifecycle yet (matches the FE union type).</summary>
    public string Status { get; init; } = "pending";

    public bool IsMandatory { get; init; }
    public int SortOrder { get; init; }
}
