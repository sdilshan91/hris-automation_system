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
    /// Optional link to a <c>SalaryGrade</c> (ISSUE-021). Stored as a nullable UUID column
    /// WITHOUT a hard DB FK constraint on purpose — legacy rows may hold arbitrary free-form
    /// GUIDs, so the link is validated service-side on write (<c>JobTitleService.ValidateGradeAsync</c>:
    /// the grade must resolve to an active, in-tenant SalaryGrade) rather than by a schema constraint.
    /// </summary>
    public Guid? GradeId { get; set; }

    /// <summary>
    /// Whether the job title is active (FR-5, BR-3).
    /// Deactivated job titles are hidden from assignment dropdowns but visible in admin views.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
