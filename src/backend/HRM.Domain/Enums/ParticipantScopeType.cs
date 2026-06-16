namespace HRM.Domain.Enums;

/// <summary>
/// How a cycle's participant set is resolved at creation (US-PRF-004 FR-3). The scope is evaluated ONCE when
/// the cycle is created (snapshotting the matching employees into cycle_participant rows); it is not a live
/// filter. Stored as a string column on the cycle.
/// </summary>
public enum ParticipantScopeType
{
    /// <summary>All active employees in the tenant.</summary>
    AllEmployees = 0,

    /// <summary>Employees in one or more specified departments.</summary>
    Departments = 1,

    /// <summary>
    /// Employees in one or more specified grades/bands. SEAM: Core HR has no grade/band field yet, so this
    /// scope is accepted by the contract but resolves to an empty set until US-CHR adds grades. Documented
    /// in the vault. Use <see cref="CustomList"/> meanwhile.
    /// </summary>
    Grades = 2,

    /// <summary>An explicit custom list of employee IDs.</summary>
    CustomList = 3,
}
