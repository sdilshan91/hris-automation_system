using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Defines a rule-based leave entitlement policy mapping leave types to
/// specific employee dimensions (department, job title, employment type, tenure).
/// US-LV-002 FR-1, FR-2: Entitlement rules with priority/specificity engine.
/// </summary>
public sealed class LeaveEntitlementRule : BaseEntity
{
    /// <summary>
    /// FK to the leave type this rule applies to (required).
    /// </summary>
    public Guid LeaveTypeId { get; set; }

    /// <summary>
    /// FK to department (nullable). When set, rule applies only to employees in this department.
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// FK to job title (nullable). When set, rule applies only to employees with this job title.
    /// </summary>
    public Guid? JobTitleId { get; set; }

    /// <summary>
    /// Placeholder for job level dimension (US-LV-002 FR-1).
    /// Stored as nullable UUID WITHOUT a hard FK because no JobLevel entity exists.
    /// TODO(job-level): Wire FK when JobLevel entity is created.
    /// </summary>
    public Guid? JobLevelId { get; set; }

    /// <summary>
    /// Employment type dimension (nullable). When set, rule applies only to matching employment type.
    /// </summary>
    public EmploymentType? EmploymentType { get; set; }

    /// <summary>
    /// Minimum tenure in months for this rule to apply (nullable, inclusive).
    /// </summary>
    public int? TenureMinMonths { get; set; }

    /// <summary>
    /// Maximum tenure in months for this rule to apply (nullable, exclusive).
    /// </summary>
    public int? TenureMaxMonths { get; set; }

    /// <summary>
    /// Number of leave days entitled under this rule.
    /// </summary>
    public decimal EntitlementDays { get; set; }

    /// <summary>
    /// Explicit priority for conflict resolution (lower number = higher priority).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Date from which this rule is effective (inclusive).
    /// </summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>
    /// Date until which this rule is effective (inclusive, nullable = no end date).
    /// </summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>
    /// Whether this rule is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ── Navigation ─────────────────────────────────────────────────

    public LeaveType? LeaveType { get; set; }
    public Department? Department { get; set; }
    public JobTitle? JobTitle { get; set; }
}
