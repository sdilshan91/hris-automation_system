namespace HRM.Domain.Enums;

/// <summary>
/// The kind of performance-based recommendation (US-PRF-010 FR-1). Promotion / Increment require a target grade
/// + effective date (BR-5); Bonus / Increment carry a monetary amount or percentage that feeds the budget
/// tracking (FR-8/BR-4) and, on approval, the downstream Payroll integration (BR-6). Stored as a string column.
/// </summary>
public enum RecommendationType
{
    /// <summary>Promote the employee to a higher grade/band — requires target grade + effective date (BR-5).</summary>
    Promotion = 0,

    /// <summary>A one-time bonus (amount or % of compensation). Feeds Payroll as a one-time earning on approval (BR-6).</summary>
    Bonus = 1,

    /// <summary>A salary increment (amount or %). Feeds Payroll / Core HR compensation on approval (BR-6).</summary>
    Increment = 2,

    /// <summary>Nominate the employee for a training course. Creates a Training-module record on approval (BR-6).</summary>
    TrainingNomination = 3,

    /// <summary>A lateral move to a different role/team at the same grade.</summary>
    LateralMove = 4,

    /// <summary>Refer the employee to a Performance Improvement Plan (links to US-PRF-008).</summary>
    PipReferral = 5,

    /// <summary>A tenant-defined custom recommendation type (FR-1).</summary>
    Custom = 6,
}
