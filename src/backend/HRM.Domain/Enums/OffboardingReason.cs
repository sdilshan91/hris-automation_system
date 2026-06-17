namespace HRM.Domain.Enums;

/// <summary>
/// Reason a departing employee is being offboarded (US-ONB-005 §7, BR-1). Stored as a string column.
/// </summary>
public enum OffboardingReason
{
    /// <summary>The employee resigned (resignation accepted).</summary>
    Resignation = 0,

    /// <summary>The employer terminated employment.</summary>
    Termination = 1,

    /// <summary>A fixed-term contract reached its end.</summary>
    ContractEnd = 2,

    /// <summary>The employee retired.</summary>
    Retirement = 3,
}
