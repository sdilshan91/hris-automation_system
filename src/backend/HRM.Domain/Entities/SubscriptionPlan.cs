namespace HRM.Domain.Entities;

/// <summary>
/// A platform-level subscription plan (US-ADM-001). This is a SYSTEM table — it does NOT inherit
/// <see cref="BaseEntity"/> and is therefore NOT tenant-scoped (no global query filter): plans are shared
/// catalog rows that every tenant selects from at provisioning time.
///
/// <para>This is intentionally MINIMAL — only what tenant provisioning needs to pick a plan and derive the
/// trial length. Full plan CRUD / pricing tiers / feature flags are owned by a later story (US-ADM-009).</para>
/// </summary>
public sealed class SubscriptionPlan
{
    public Guid Id { get; set; }

    /// <summary>Human-readable plan name, e.g. "Starter", "Professional", "Enterprise".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable unique slug, e.g. "starter". Stored on the tenant's <c>PlanId</c> when chosen.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Monthly price (billing is offline in Phase 1 — this is informational).</summary>
    public decimal PriceMonthly { get; set; }

    /// <summary>Default trial length in days. When &gt; 0 the provisioned tenant starts in Trial status (BR-3).</summary>
    public int TrialDays { get; set; }

    /// <summary>Maximum employees allowed under this plan. Null means unlimited.</summary>
    public int? MaxEmployees { get; set; }

    /// <summary>Whether this plan can be selected for new tenants (AC validation references active plans only).</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
