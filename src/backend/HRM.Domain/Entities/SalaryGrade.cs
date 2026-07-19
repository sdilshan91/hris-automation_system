namespace HRM.Domain.Entities;

/// <summary>
/// Represents a salary grade (pay band) within a tenant's compensation structure (Payroll domain, ISSUE-021).
/// A salary grade defines a min/mid/max amount band in a given currency and is the entity that
/// <see cref="JobTitle.GradeId"/> links to. Supports tenant-scoped CRUD and soft-delete/deactivation.
/// </summary>
public sealed class SalaryGrade : BaseEntity
{
    /// <summary>
    /// Short code for the salary grade, unique within a tenant (case-insensitive). Example: "G1", "SNR-2".
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name of the salary grade. Example: "Grade 1 - Associate".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Minimum amount of the pay band. Must be zero or positive and not exceed <see cref="MaxAmount"/>.
    /// </summary>
    public decimal MinAmount { get; set; }

    /// <summary>
    /// Optional midpoint amount of the pay band. When provided, must satisfy Min &lt;= Mid &lt;= Max.
    /// </summary>
    public decimal? MidAmount { get; set; }

    /// <summary>
    /// Maximum amount of the pay band. Must be zero or positive and not be less than <see cref="MinAmount"/>.
    /// </summary>
    public decimal MaxAmount { get; set; }

    /// <summary>
    /// ISO 4217 3-letter currency code for the band amounts, e.g. "USD".
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional free-text description of the salary grade.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the salary grade is active. Deactivated grades are hidden from the active list and
    /// cannot be linked to job titles, but are retained for historical reference.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
