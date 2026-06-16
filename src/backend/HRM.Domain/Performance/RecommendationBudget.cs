using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// A bonus/increment budget for one appraisal cycle (US-PRF-010 FR-8). HR sets an <see cref="AllocatedAmount"/>;
/// the <see cref="ConsumedAmount"/> is the running sum of the monetary amounts of the SUBMITTED+ recommendations
/// charged against it. BR-4 is a SOFT warning: when consumed exceeds allocated the service flags a warning but
/// never hard-blocks the submission, so HR can proceed with justification. Budget tracking is optional — a cycle
/// with no budget row simply has no warning. At most one budget per cycle per tenant.
///
/// ENCRYPTION SEAM (NFR-3): <see cref="AllocatedAmount"/>/<see cref="ConsumedAmount"/> are financial figures and
/// are stored as plain numeric today. The codebase has no field/PII (pgcrypto) encryption mechanism; building one
/// is an infra concern out of scope here (same decision as US-PRF-008). TODO(US-PLT PII-encryption).
///
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>
/// (NFR-2). Maps to the "recommendation_budget" table.
/// </summary>
public sealed class RecommendationBudget : BaseEntity
{
    /// <summary>The appraisal cycle this budget belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>Optional human label for the budget, e.g. "FY2026 Bonus Pool".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SENSITIVE (NFR-3 encryption seam). The total budget HR allocated for bonus/increment payouts (FR-8).</summary>
    public decimal AllocatedAmount { get; set; }

    /// <summary>
    /// SENSITIVE (NFR-3 encryption seam). The running consumed amount — the sum of the monetary amounts of the
    /// recommendations charged against this budget (FR-8). Maintained by the service as recommendations are
    /// submitted/approved and reversed when rejected.
    /// </summary>
    public decimal ConsumedAmount { get; set; }

    /// <summary>ISO currency code for display (e.g. "USD"). Default "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optimistic concurrency token (prevents lost budget updates under concurrent submissions). Maps to the
    /// PostgreSQL xmin system column via the Npgsql row-version convention (no schema DDL); InMemory ignores it.
    /// </summary>
    public uint Version { get; set; }

    /// <summary>Remaining budget (FR-8). Negative when over-allocated (BR-4 soft-warning territory).</summary>
    public decimal RemainingAmount => AllocatedAmount - ConsumedAmount;

    /// <summary>True when consumed has exceeded allocated (BR-4 soft warning — never a hard block).</summary>
    public bool IsExceeded => ConsumedAmount > AllocatedAmount;
}
