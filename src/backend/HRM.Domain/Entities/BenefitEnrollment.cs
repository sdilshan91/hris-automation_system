using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A tenant-scoped employee election into a <see cref="BenefitPlan"/> (US-TRN-003 FR-2). Exactly one
/// <see cref="BenefitEnrollmentStatus.Active"/> enrollment may exist per (plan, employee) — enforced by a partial
/// unique index (BR-3, NFR-3). Termination is a soft state change to <see cref="BenefitEnrollmentStatus.Terminated"/>
/// with an <see cref="EndDate"/> (BR-5), preserving history.
/// </summary>
public sealed class BenefitEnrollment : BaseEntity
{
    /// <summary>The elected plan (FK → <see cref="BenefitPlan"/>).</summary>
    public Guid BenefitPlanId { get; set; }

    /// <summary>The enrolled employee (FK → <see cref="Employee"/>).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Enrollment lifecycle status (FR-2).</summary>
    public BenefitEnrollmentStatus Status { get; set; } = BenefitEnrollmentStatus.Active;

    /// <summary>Elected coverage tier (FR-2).</summary>
    public CoverageLevel CoverageLevel { get; set; } = CoverageLevel.EmployeeOnly;

    /// <summary>Date the coverage takes effect (today, or the plan's future effective-from) (AC-3).</summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>Date the coverage ends; set on termination (BR-5).</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>UTC timestamp of the election (AC-3).</summary>
    public DateTime ElectedAt { get; set; }

    /// <summary>The user who recorded the election — the employee (self-service) or an HR officer (AC-3).</summary>
    public Guid ElectedBy { get; set; }
}
