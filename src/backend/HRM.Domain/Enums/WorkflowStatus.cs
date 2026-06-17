namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of an approval workflow definition (US-ADM-007 AC-1/FR-6).
/// </summary>
public enum WorkflowStatus
{
    /// <summary>Saved but not yet the live workflow for its entity type.</summary>
    Draft = 0,

    /// <summary>The single live workflow for its entity type (BR-2). New requests use this version.</summary>
    Active = 1,

    /// <summary>Archived (BR-6 / FR-6) — no longer applies to new requests, does not count toward the plan limit (FR-4).</summary>
    Archived = 2
}
