namespace HRM.Domain.Enums;

/// <summary>
/// The escalation action configured for a PIP that ends in <see cref="PipStatus.NotMet"/> (US-PRF-008 BR-6/AC-5).
/// Configurable per tenant; recorded on the PIP at creation and confirmed by HR at escalation time. Stored as a
/// string column.
/// </summary>
public enum PipEscalationAction
{
    /// <summary>No escalation action configured.</summary>
    None = 0,

    /// <summary>Reassign the employee to a different role/team.</summary>
    Reassignment = 1,

    /// <summary>Demote the employee.</summary>
    Demotion = 2,

    /// <summary>Do not renew the employee's contract.</summary>
    ContractNonRenewal = 3,

    /// <summary>Recommend termination of employment.</summary>
    TerminationRecommendation = 4,
}
