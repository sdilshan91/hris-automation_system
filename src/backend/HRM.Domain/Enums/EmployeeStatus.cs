namespace HRM.Domain.Enums;

/// <summary>
/// Employee status values (US-CHR-001 BR-3, US-CHR-009 FR-1).
/// Default on creation is Active unless explicitly set to Probation.
/// US-CHR-009: Added Suspended state for the full 5-state lifecycle.
/// </summary>
public enum EmployeeStatus
{
    Active = 0,
    Probation = 1,
    Inactive = 2,
    Terminated = 3,
    Suspended = 4,
}
