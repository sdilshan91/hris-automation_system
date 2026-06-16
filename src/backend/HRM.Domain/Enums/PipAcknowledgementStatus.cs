namespace HRM.Domain.Enums;

/// <summary>
/// Employee acknowledgement state of an initiated PIP (US-PRF-008 BR-4). The employee must acknowledge PIP
/// initiation (similar to the US-PRF-006 review sign-off); if they do not acknowledge within 5 business days the
/// PIP proceeds with a <see cref="NotAcknowledged"/> flag. Stored as a string column.
/// </summary>
public enum PipAcknowledgementStatus
{
    /// <summary>Awaiting the employee's acknowledgement (the default once the PIP is Active).</summary>
    Pending = 0,

    /// <summary>The employee acknowledged the PIP (immutable acknowledgement record written, BR-4).</summary>
    Acknowledged = 1,

    /// <summary>The employee did not acknowledge within 5 business days; the PIP proceeds flagged (BR-4).</summary>
    NotAcknowledged = 2,
}
