namespace HRM.Domain.Entities;

/// <summary>
/// Represents a job title within a tenant's organizational structure.
/// Supports optional salary grade linking and soft-delete.
/// US-CHR-005: Create and Manage Job Titles and Positions.
/// </summary>
public sealed class JobTitle : BaseEntity
{
    /// <summary>
    /// Job title name, unique within a tenant (FR-2, BR-1).
    /// </summary>
    public string TitleName { get; set; } = string.Empty;

    /// <summary>
    /// Optional free-text description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional salary grade link. Stored as a nullable UUID column
    /// WITHOUT a hard FK constraint because the Grade entity does not
    /// exist yet (it belongs to the Payroll module).
    /// TODO(payroll): wire grade_id FK to Grade entity once it exists.
    /// </summary>
    public Guid? GradeId { get; set; }

    /// <summary>
    /// Whether the job title is active (FR-5, BR-3).
    /// Deactivated job titles are hidden from assignment dropdowns but visible in admin views.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
