namespace HRM.Domain.Enums;

/// <summary>
/// Employee status values (US-CHR-001 BR-3).
/// Default on creation is Active unless explicitly set to Probation.
/// </summary>
public enum EmployeeStatus
{
    Active = 0,
    Probation = 1,
    Inactive = 2,
    Terminated = 3,
}
