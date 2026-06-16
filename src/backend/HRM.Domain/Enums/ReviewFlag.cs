namespace HRM.Domain.Enums;

/// <summary>
/// Optional disposition a manager may flag an employee with on their review (US-PRF-003 FR-6).
/// Stored as a string column; null/None when no flag is set.
/// </summary>
public enum ReviewFlag
{
    /// <summary>No flag set.</summary>
    None = 0,

    /// <summary>Flagged for recognition / reward (FR-6).</summary>
    Recognition = 1,

    /// <summary>Flagged for promotion consideration (FR-6).</summary>
    Promotion = 2,

    /// <summary>Flagged for a performance improvement plan (FR-6).</summary>
    Pip = 3,
}
