namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a Full-and-Final (F&amp;F) settlement (ISSUE-294 Phase 1). Phase 1 only ever produces a
/// <see cref="Computed"/> settlement (the amount is calculated + persisted at offboarding completion). Downstream
/// states (Approved / Paid) are deferred to a later phase; the enum is stored as a string so adding them is
/// non-breaking.
/// </summary>
public enum FinalSettlementStatus
{
    /// <summary>The settlement amounts have been computed and persisted (Phase 1 terminal state).</summary>
    Computed = 0,
}
