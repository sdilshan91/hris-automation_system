namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a performance-based recommendation (US-PRF-010 §7/AC-3/FR-4). A recommendation starts as
/// <see cref="Draft"/> (manually created or auto-suggested), is <see cref="Submitted"/> by HR which routes it
/// through the approval workflow (<see cref="PendingApproval"/>), and terminates in <see cref="Approved"/> or
/// <see cref="Rejected"/>. Approval feeds the downstream integration seam (BR-6). Stored as a string column.
/// </summary>
public enum RecommendationStatus
{
    /// <summary>Created/suggested but not yet submitted (BR-3 — auto-generated suggestions land here). Default.</summary>
    Draft = 0,

    /// <summary>Submitted by HR; if no approvers are configured this is the terminal "submitted" state.</summary>
    Submitted = 1,

    /// <summary>Routed through the approval workflow and awaiting an approver decision (FR-4).</summary>
    PendingApproval = 2,

    /// <summary>All approvers approved — feeds the downstream integration seam (BR-6).</summary>
    Approved = 3,

    /// <summary>An approver rejected the recommendation (FR-4).</summary>
    Rejected = 4,
}
