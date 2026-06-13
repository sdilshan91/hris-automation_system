namespace HRM.Domain.Enums;

/// <summary>
/// Gender applicability for a leave type (US-LV-001 FR-2).
/// Determines which employees can see and apply for the leave type.
/// </summary>
public enum LeaveTypeGender
{
    All = 0,
    Male = 1,
    Female = 2,
}
