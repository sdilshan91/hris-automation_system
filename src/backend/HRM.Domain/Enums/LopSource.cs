namespace HRM.Domain.Enums;

/// <summary>
/// Origin of a Loss-of-Pay (LOP) leave request (US-LV-011 §7, FR-4).
/// Stored as a string in the leave_request.lop_source column (varchar(20)), matching the
/// leave enum-as-string convention. Null on a normal (non-LOP) leave request.
/// </summary>
public enum LopSource
{
    /// <summary>The employee applied for leave with insufficient balance and confirmed LOP (AC-1).</summary>
    EmployeeRequest = 0,

    /// <summary>The absenteeism reconciliation job generated it for an unaccounted absence (AC-2, FR-2).</summary>
    SystemGenerated = 1,

    /// <summary>HR manually assigned LOP days to the employee (AC-3, FR-3).</summary>
    HrAssigned = 2,

    /// <summary>A compulsory-leave (company shutdown) day fell back to LOP for insufficient balance (FR-6, BR-4).</summary>
    Compulsory = 3,
}
