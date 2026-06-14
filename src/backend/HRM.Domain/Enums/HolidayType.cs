namespace HRM.Domain.Enums;

/// <summary>
/// Classification of a holiday (US-LV-007 FR-2).
/// Stored as a string in the database.
/// </summary>
public enum HolidayType
{
    /// <summary>
    /// Public holiday applying to all employees in the tenant/location (BR-2).
    /// Excluded from leave day calculations.
    /// </summary>
    Public = 0,

    /// <summary>
    /// Restricted holiday that may require employees to apply (BR-2).
    /// </summary>
    Restricted = 1,

    /// <summary>
    /// Optional holiday that may count against a separate leave type if configured (BR-3).
    /// </summary>
    Optional = 2,
}
