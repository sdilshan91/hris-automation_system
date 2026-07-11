namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a benefit plan (US-TRN-002 FR-1). Stored as a string column. Only <see cref="Active"/>
/// plans within their effective window are offered for new enrollments (US-TRN-003). Legal transitions are
/// enforced by the service (BenefitPlanService.IsLegalTransition, AC-2/AC-6):
/// Draft → Active/Archived; Active → Inactive/Archived; Inactive → Active/Archived; Archived is terminal.
/// </summary>
public enum BenefitPlanStatus
{
    Draft = 0,
    Active = 1,
    Inactive = 2,
    Archived = 3,
}
