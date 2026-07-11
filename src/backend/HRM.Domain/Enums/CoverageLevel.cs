namespace HRM.Domain.Enums;

/// <summary>
/// The coverage tier elected for a benefit enrollment (US-TRN-003 FR-2). Stored as a string column.
/// Premium calculation per tier is out of scope for v1 (epic-flagged future).
/// </summary>
public enum CoverageLevel
{
    EmployeeOnly = 0,
    EmployeeSpouse = 1,
    Family = 2,
}
