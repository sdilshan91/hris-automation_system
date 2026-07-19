namespace HRM.Domain.Entities;

/// <summary>
/// Education history entry for an employee (US-CHR-002 / DF-39).
/// Tenant-scoped via BaseEntity.TenantId. Full-replace managed in UpdateProfileAsync.
/// </summary>
public sealed class EmployeeEducation : BaseEntity
{
    /// <summary>
    /// FK to the employee this education entry belongs to.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// Educational institution / school name (required).
    /// </summary>
    public string Institution { get; set; } = string.Empty;

    /// <summary>
    /// Degree / qualification obtained (required).
    /// </summary>
    public string Degree { get; set; } = string.Empty;

    /// <summary>
    /// Field of study / major (optional).
    /// </summary>
    public string? FieldOfStudy { get; set; }

    /// <summary>
    /// Start year (free-text, e.g. "2015"; optional).
    /// </summary>
    public string? StartYear { get; set; }

    /// <summary>
    /// End year (free-text, e.g. "2019"; optional).
    /// </summary>
    public string? EndYear { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
}
