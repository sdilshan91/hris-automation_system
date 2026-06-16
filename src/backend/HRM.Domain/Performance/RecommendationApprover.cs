using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// One step in a <see cref="Recommendation"/>'s approval chain (US-PRF-010 FR-4). Models the same approver-chain +
/// per-step status approach the leave (US-LV-005) and attendance (US-ATT-004) approval flows use, rather than a
/// new workflow engine: an ordered list of approver employees, each with an outcome. Sequential approval walks the
/// chain by <see cref="StepOrder"/>; the recommendation is Approved only when every step is Approved, and Rejected
/// as soon as any step rejects. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter +
/// <c>TenantInterceptor</c> (NFR-2). Maps to the "recommendation_approver" table.
/// </summary>
public sealed class RecommendationApprover : BaseEntity
{
    /// <summary>The recommendation this approver step belongs to (FK, required).</summary>
    public Guid RecommendationId { get; set; }

    /// <summary>The approver's employee id (FK to <see cref="Employee"/>, required).</summary>
    public Guid ApproverEmployeeId { get; set; }

    /// <summary>The order of this step in the chain (0-based). Sequential approval honours this order.</summary>
    public int StepOrder { get; set; }

    /// <summary>The approver's decision: Pending until they act, then Approved or Rejected.</summary>
    public RecommendationApproverDecision Decision { get; set; } = RecommendationApproverDecision.Pending;

    /// <summary>UTC timestamp the approver acted. Null while Pending.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Optional comment captured with the approver's decision.</summary>
    public string? Comment { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public Recommendation? Recommendation { get; set; }
}

/// <summary>An approval-chain step's outcome (US-PRF-010 FR-4).</summary>
public enum RecommendationApproverDecision
{
    /// <summary>Awaiting this approver's decision.</summary>
    Pending = 0,

    /// <summary>This approver approved their step.</summary>
    Approved = 1,

    /// <summary>This approver rejected the recommendation.</summary>
    Rejected = 2,
}
