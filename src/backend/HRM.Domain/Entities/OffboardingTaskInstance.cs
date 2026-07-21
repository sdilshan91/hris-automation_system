using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A single exit/clearance task within an <see cref="OffboardingInstance"/> (US-ONB-005 AC-1/AC-3). Each task
/// belongs to a <see cref="ClearanceCategory"/> (FR-3), is owned by a responsible role, has a due date
/// calculated as last_working_day - offset_days (FR-2), and may carry a department clearance decision
/// (<see cref="ClearanceStatus"/>, AC-3) and/or a linked asset to return (AC-2/BR-3). Mandatory tasks block
/// completion until approved/complete (BR-2/AC-5). Soft-deletable. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the global query filter. Maps to "offboarding_task_instance".
/// </summary>
public sealed class OffboardingTaskInstance : BaseEntity
{
    /// <summary>FK to the owning <see cref="OffboardingInstance"/>.</summary>
    public Guid OffboardingInstanceId { get; set; }

    /// <summary>The template task this was cloned from (null for built-in default tasks).</summary>
    public Guid? SourceTemplateTaskId { get; set; }

    /// <summary>Clearance category / department this task belongs to (FR-3, AC-3).</summary>
    public ClearanceCategory ClearanceCategory { get; set; }

    /// <summary>Task title (AC-1).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional task description.</summary>
    public string? Description { get; set; }

    /// <summary>Role responsible for the task / clearance (AC-1). Mirrors the onboarding responsible role set.</summary>
    public OnboardingResponsibleRole ResponsibleRole { get; set; } = OnboardingResponsibleRole.HR;

    /// <summary>Resolved named responsible user (Manager → reporting manager, Employee → the departing hire).</summary>
    public Guid? ResponsibleUserId { get; set; }

    /// <summary>Calculated due date: last_working_day - offset_days (FR-2).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Task status. New tasks start <see cref="OnboardingTaskStatus.Pending"/>.</summary>
    public OnboardingTaskStatus Status { get; set; } = OnboardingTaskStatus.Pending;

    /// <summary>Mandatory flag (BR-2/AC-5). Mandatory tasks must be approved + complete before completion.</summary>
    public bool IsMandatory { get; set; }

    /// <summary>Sort order within the offboarding/category.</summary>
    public int SortOrder { get; set; }

    // ── AC-3 clearance decision ────────────────────────────────────

    /// <summary>The department clearance decision (AC-3). Null until a decision is recorded.</summary>
    public ClearanceStatus? ClearanceStatus { get; set; }

    /// <summary>Optional free-text remarks supplied with the clearance decision (§7, max 1000).</summary>
    public string? Remarks { get; set; }

    /// <summary>When the clearance/task action was recorded (AC-2/AC-3). Null until acted on.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>The user who recorded the clearance/task action (AC-2/AC-3 audit). Null until acted on.</summary>
    public Guid? CompletedByUserId { get; set; }

    // ── AC-2 / BR-3 asset return ───────────────────────────────────

    /// <summary>The asset this task tracks the return of (AC-2/BR-3). Cross-module ref to Asset; no hard FK.</summary>
    public Guid? LinkedAssetId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public OffboardingInstance? OffboardingInstance { get; set; }
}
