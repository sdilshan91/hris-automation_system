namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a company asset in the lite asset register (US-ONB-004 FR-2). An asset must be
/// <see cref="Available"/> before it can be issued (BR-1/FR-3); issuance moves it to <see cref="Assigned"/>
/// (FR-4). Return/disposal (BR-4) are handled by the offboarding flow (out of scope here) but the states
/// are modelled so the register is forward-compatible. Stored as a string column.
/// </summary>
public enum AssetStatus
{
    /// <summary>In the register and not assigned to anyone — eligible for issuance (FR-3).</summary>
    Available = 0,

    /// <summary>Currently issued to an employee (FR-4). Cannot be re-issued until returned (BR-1).</summary>
    Assigned = 1,

    /// <summary>Returned during offboarding and pending re-issue/disposal (BR-4).</summary>
    Returned = 2,

    /// <summary>Disposed/written off and no longer issuable (BR-4).</summary>
    Disposed = 3,
}
