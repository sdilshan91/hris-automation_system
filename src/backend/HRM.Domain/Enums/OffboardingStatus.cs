namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of an <see cref="Entities.OffboardingInstance"/> (US-ONB-005 §7). New instances start
/// <see cref="InProgress"/>; completion is irreversible (BR-6). Stored as a string column.
/// </summary>
public enum OffboardingStatus
{
    /// <summary>Initiated and clearance is being collected (AC-1, default).</summary>
    InProgress = 0,

    /// <summary>All mandatory clearances approved + completed; account deactivated, F&amp;F triggered (AC-4, BR-6).</summary>
    Completed = 1,
}
