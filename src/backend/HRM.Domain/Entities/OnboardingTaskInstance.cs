using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A single concrete task assigned to a new hire as part of an <see cref="OnboardingChecklistInstance"/>
/// (US-ONB-002 AC-2). Cloned from a template task at assignment time (or added ad-hoc per AC-4/FR-5) with
/// a calculated <see cref="DueDate"/>, a resolved responsible party (FR-3), and a status that starts
/// <see cref="OnboardingTaskStatus.Pending"/>. Soft-deletable (AC-4 removed tasks) — but mandatory tasks
/// cannot be removed (BR-3). Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the global query filter.
/// Maps to the "onboarding_task_instance" table.
/// </summary>
public sealed class OnboardingTaskInstance : BaseEntity
{
    /// <summary>FK to the owning <see cref="OnboardingChecklistInstance"/>.</summary>
    public Guid ChecklistInstanceId { get; set; }

    /// <summary>The template task this was cloned from (null for ad-hoc tasks, FR-5).</summary>
    public Guid? SourceTemplateTaskId { get; set; }

    /// <summary>Task title (AC-2).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional task description.</summary>
    public string? Description { get; set; }

    /// <summary>Free-text category label, e.g. "Documentation", "IT Setup".</summary>
    public string? Category { get; set; }

    /// <summary>Role responsible for the task (FR-3). Defaults to HR.</summary>
    public OnboardingResponsibleRole ResponsibleRole { get; set; } = OnboardingResponsibleRole.HR;

    /// <summary>
    /// Resolved named responsible user (FR-3): Manager → reporting manager, HR → assigning officer,
    /// Employee → the hire, IT → resolved IT user (when a single one exists). Cross-module ref, no hard FK.
    /// </summary>
    public Guid? ResponsibleUserId { get; set; }

    /// <summary>Calculated due date: StartDate + due_offset_days (FR-2, BR-4).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Status (AC-2). New task instances start <see cref="OnboardingTaskStatus.Pending"/>.</summary>
    public OnboardingTaskStatus Status { get; set; } = OnboardingTaskStatus.Pending;

    /// <summary>Mandatory flag (BR-3). Mandatory tasks cannot be removed from the assigned checklist.</summary>
    public bool IsMandatory { get; set; }

    /// <summary>Sort order within the checklist/category.</summary>
    public int SortOrder { get; set; }

    // ── US-ONB-003: self-service completion ────────────────────────

    /// <summary>
    /// US-ONB-003 AC-4/FR-3: when true the employee MUST attach a document to complete this task
    /// (e.g. "Submit ID proof"). Propagated from the template task at assignment time.
    /// </summary>
    public bool RequiresDocument { get; set; }

    /// <summary>US-ONB-003 AC-3: when the task was marked completed (UTC). Null while not completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>US-ONB-003 AC-3: the user (the new hire) who completed the task. Null while not completed.</summary>
    public Guid? CompletedByUserId { get; set; }

    /// <summary>US-ONB-003 FR-2: optional free-text comment supplied on completion (max 500).</summary>
    public string? CompletionComment { get; set; }

    /// <summary>US-ONB-003 AC-4: tenant-isolated storage key of the uploaded document (null if none).</summary>
    public string? AttachmentStorageKey { get; set; }

    /// <summary>US-ONB-003 AC-4: original file name of the uploaded document (for download display).</summary>
    public string? AttachmentFileName { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public OnboardingChecklistInstance? ChecklistInstance { get; set; }
}
