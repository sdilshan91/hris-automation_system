namespace HRM.Domain.Entities;

/// <summary>
/// A per-tenant override of a single plan limit (US-ADM-009 FR-4/AC-5). System/platform data — like
/// <see cref="SubscriptionPlan"/> it does NOT inherit <see cref="BaseEntity"/> and is NOT tenant-scoped via a
/// global query filter (it references a tenant by id but is managed only from the system-admin console). The
/// runtime resolution order is: a non-expired override for the tenant+key WINS over the plan field
/// (see <c>HRM.Domain.Authorization.PlanLimitResolver</c>).
/// </summary>
public sealed class PlanLimitOverride
{
    public Guid Id { get; set; }

    /// <summary>The tenant this override applies to.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The limit key being overridden, e.g. "max_employees". Matches the canonical limit keys in
    /// <c>HRM.Domain.Authorization.PlanLimitKeys</c>.
    /// </summary>
    public string LimitKey { get; set; } = string.Empty;

    /// <summary>
    /// The override value. <b>NULL means "unlimited"</b> for this tenant+key (US-ADM-009 BR-3) — i.e. the
    /// override is present but removes the cap. Stored as a 64-bit integer to accommodate large quotas.
    /// </summary>
    public long? Value { get; set; }

    /// <summary>When the override expires (UTC). Null = never expires. An expired override is ignored (FR-4).</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the override was last edited. Null until first update.</summary>
    public DateTime? UpdatedAt { get; set; }
}
