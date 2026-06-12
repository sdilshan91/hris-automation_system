namespace HRM.Domain.Enums;

/// <summary>
/// Employment type reference values (US-CHR-005 FR-6).
/// Stored as an enum rather than a full entity in Phase 1.
/// Used alongside job titles when assigning employees.
/// </summary>
public enum EmploymentType
{
    FullTime = 0,
    PartTime = 1,
    Contract = 2,
    Intern = 3,
}
