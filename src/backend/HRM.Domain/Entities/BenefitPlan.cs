using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A tenant-scoped benefit plan in the catalog (US-TRN-002 FR-1). Employees enroll in <see cref="Active"/>
/// plans within their effective window (US-TRN-003). Costs are stored as <c>decimal</c> with a currency code
/// (NFR-3). A plan with enrollments is archivable, not hard-deletable (BR-4) — enforced in US-TRN-003.
/// </summary>
public sealed class BenefitPlan : BaseEntity, IAuditExempt
{
    /// <summary>Plan name (required) (FR-1, AC-1).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Benefit category (FR-1).</summary>
    public BenefitType Type { get; set; } = BenefitType.Health;

    /// <summary>Optional free-text description of the plan.</summary>
    public string? Description { get; set; }

    /// <summary>Optional free-text coverage details.</summary>
    public string? CoverageDetails { get; set; }

    /// <summary>Optional employer-borne cost (per period). Non-negative when set (AC-1).</summary>
    public decimal? EmployerCost { get; set; }

    /// <summary>Optional employee-borne cost (per period). Non-negative when set (AC-1).</summary>
    public decimal? EmployeeCost { get; set; }

    /// <summary>ISO-4217 currency code for the cost fields. Defaults to the tenant's currency (NFR-3).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Effective start date of the plan (AC-1).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Effective end date (null = open-ended). Must be &gt;= EffectiveFrom when set (AC-1).</summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Optional enrollment-window open date (US-TRN-003). Nullable here; unused by TRN-002.</summary>
    public DateOnly? EnrollmentOpensAt { get; set; }

    /// <summary>Optional enrollment-window close date (US-TRN-003). Nullable here; unused by TRN-002.</summary>
    public DateOnly? EnrollmentClosesAt { get; set; }

    /// <summary>Lifecycle status. Only <see cref="BenefitPlanStatus.Active"/> plans are enrollable (BR-2).</summary>
    public BenefitPlanStatus Status { get; set; } = BenefitPlanStatus.Draft;
}
