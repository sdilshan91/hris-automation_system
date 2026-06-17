using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A single task within an <see cref="OnboardingChecklistTemplate"/> (US-ONB-001 FR-3). Grouped by a
/// free-text category, with an ordered position, a responsible party (role and/or named user), a due
/// offset in days from the joining date, and a mandatory flag (AC-4). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> and the EF global query filter + <c>TenantInterceptor</c>. Maps to
/// the "onboarding_template_task" table.
/// </summary>
public sealed class OnboardingTemplateTask : BaseEntity
{
    /// <summary>FK to the owning <see cref="OnboardingChecklistTemplate"/>.</summary>
    public Guid TemplateId { get; set; }

    /// <summary>Task title (FR-3, required, min 3 / max 200).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional task description (FR-3, max 1000).</summary>
    public string? Description { get; set; }

    /// <summary>Free-text category label, e.g. "Documentation", "IT Setup" (FR-2/FR-3, max 100).</summary>
    public string? Category { get; set; }

    /// <summary>Role responsible for the task (FR-3). Defaults to HR.</summary>
    public OnboardingResponsibleRole ResponsibleRole { get; set; } = OnboardingResponsibleRole.HR;

    /// <summary>
    /// Optional named responsible user (FR-3). When set, overrides/augments the role-based assignment.
    /// Cross-module reference (no hard FK); tenant-scoped existence validated in the service.
    /// </summary>
    public Guid? ResponsibleUserId { get; set; }

    /// <summary>Due offset in days from the date of joining (FR-3, BR-3, &gt;= 0; 0 = same day).</summary>
    public int DueOffsetDays { get; set; }

    /// <summary>Mandatory flag (AC-4). Mandatory tasks cannot be skipped when assigned to a new hire.</summary>
    public bool IsMandatory { get; set; }

    /// <summary>Sort order within the template/category (FR-1/FR-3, &gt;= 0).</summary>
    public int SortOrder { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public OnboardingChecklistTemplate? Template { get; set; }
}
