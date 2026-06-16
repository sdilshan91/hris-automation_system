namespace HRM.Domain.Enums;

/// <summary>
/// Which party recorded a <c>ReviewSignoff</c> entry (US-PRF-006 FR-3). Stored as a string column.
/// </summary>
public enum SignoffParty
{
    /// <summary>The reviewing manager.</summary>
    Manager = 0,

    /// <summary>The employee being reviewed.</summary>
    Employee = 1,

    /// <summary>An HR actor resolving a dispute (BR-4).</summary>
    Hr = 2,
}
