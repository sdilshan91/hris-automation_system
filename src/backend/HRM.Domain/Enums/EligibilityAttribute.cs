namespace HRM.Domain.Enums;

/// <summary>
/// The Core HR employee attribute an eligibility rule tests (US-TRN-003 FR-1). Stored as a string column.
/// <see cref="JobGrade"/> maps to the employee's <c>JobTitleId</c> (there is no separate grade column);
/// <see cref="TenureDays"/> is derived as days since <c>DateOfJoining</c>.
/// </summary>
public enum EligibilityAttribute
{
    EmploymentType = 0,
    TenureDays = 1,
    Department = 2,
    JobGrade = 3,
}
