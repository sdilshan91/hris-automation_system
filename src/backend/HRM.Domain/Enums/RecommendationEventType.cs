namespace HRM.Domain.Enums;

/// <summary>
/// An action captured in the immutable, append-only <c>RecommendationEvent</c> audit log (US-PRF-010 FR-4/FR-7).
/// Mirrors the US-PRF-008 <c>PipEventType</c> pattern — each workflow action writes ONE row capturing the actor,
/// action, timestamp and optional detail. Rows are never updated/deleted. Stored as a string column.
/// </summary>
public enum RecommendationEventType
{
    /// <summary>The recommendation was created (manually or as an auto-generated suggestion).</summary>
    Created = 0,

    /// <summary>HR manually overrode the recommendation (FR-3 — justification recorded on the recommendation).</summary>
    Overridden = 1,

    /// <summary>HR submitted the recommendation into the approval workflow (AC-3).</summary>
    Submitted = 2,

    /// <summary>An approver approved their step (FR-4).</summary>
    Approved = 3,

    /// <summary>An approver rejected the recommendation (FR-4).</summary>
    Rejected = 4,

    /// <summary>
    /// The recommendation reached final approval and the downstream-integration seam was raised (BR-6 — promotions
    /// to Core HR, bonuses to Payroll, training nominations to Training). The seam is recorded, not yet wired.
    /// </summary>
    IntegrationRaised = 5,
}
