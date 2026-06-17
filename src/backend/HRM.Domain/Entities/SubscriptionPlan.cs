namespace HRM.Domain.Entities;

/// <summary>
/// A platform-level subscription plan (US-ADM-001, extended by US-ADM-009). This is a SYSTEM table — it does
/// NOT inherit <see cref="BaseEntity"/> and is therefore NOT tenant-scoped (no global query filter): plans are
/// shared catalog rows that every tenant selects from at provisioning time.
///
/// <para>US-ADM-009 extends this with the full pricing / entitlement / limit / feature-flag schema (FR-2). The
/// limit fields are nullable; <b>NULL means "unlimited"</b> (BR-3). The <c>Code</c> is the stable, IMMUTABLE
/// slug referenced by <c>Tenant.PlanId</c> (FR-3/BR-5); once created it can never change.</para>
/// </summary>
public sealed class SubscriptionPlan
{
    public Guid Id { get; set; }

    /// <summary>Human-readable plan name, e.g. "Starter", "Growth", "Business", "Enterprise".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Stable unique slug, e.g. "starter". Stored on the tenant's <c>PlanId</c> when chosen. IMMUTABLE after
    /// creation (US-ADM-009 FR-3/BR-5) — the update path rejects any attempt to change it.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Long-form plan description shown in the editor / comparison view (US-ADM-009 FR-2).</summary>
    public string? Description { get; set; }

    // ── Pricing (US-ADM-009 FR-2; billing is offline in Phase 1 — informational) ──

    /// <summary>Monthly price.</summary>
    public decimal PriceMonthly { get; set; }

    /// <summary>Yearly price (US-ADM-009 FR-2 price_per_year).</summary>
    public decimal PriceYearly { get; set; }

    /// <summary>ISO-4217 currency code for the prices (US-ADM-009 FR-2). Default "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Default trial length in days. When &gt; 0 the provisioned tenant starts in Trial status (BR-3).</summary>
    public int TrialDays { get; set; }

    // ── Visibility / status (US-ADM-009 FR-2/BR-2) ──

    /// <summary>
    /// When true the plan appears on the public pricing page (Phase 2 self-serve); when false it is a
    /// custom/enterprise plan offered only via direct provisioning (US-ADM-009 BR-2). Default false.
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>Whether this plan can be selected for new tenants. Archiving sets this false (AC-4).</summary>
    public bool IsActive { get; set; } = true;

    // ── Usage limits — NULL means unlimited (US-ADM-009 FR-2/BR-3) ──

    /// <summary>Maximum employees allowed under this plan. Null = unlimited.</summary>
    public int? MaxEmployees { get; set; }

    /// <summary>Maximum storage in gigabytes. Null = unlimited.</summary>
    public int? MaxStorageGb { get; set; }

    /// <summary>Maximum API calls per month. Null = unlimited.</summary>
    public int? MaxApiCallsPerMonth { get; set; }

    /// <summary>Maximum outbound email sends per month. Null = unlimited.</summary>
    public int? MaxEmailSendsPerMonth { get; set; }

    /// <summary>Maximum custom roles. Null = unlimited.</summary>
    public int? MaxCustomRoles { get; set; }

    /// <summary>Maximum custom fields per entity type. Null = unlimited.</summary>
    public int? MaxCustomFieldsPerEntity { get; set; }

    /// <summary>Maximum active approval workflows. Null = unlimited.</summary>
    public int? MaxWorkflows { get; set; }

    /// <summary>Number of days audit-log rows are retained before purge (US-ADM-009 FR-2). Not a "limit" — a policy value.</summary>
    public int AuditLogRetentionDays { get; set; } = 90;

    /// <summary>SLA tier label, e.g. "standard", "business", "enterprise" (US-ADM-009 FR-2). Default "standard".</summary>
    public string SlaTier { get; set; } = "standard";

    // ── Entitlements (US-ADM-009 FR-2/FR-6) ──

    /// <summary>
    /// The modules enabled for tenants on this plan (US-ADM-009 FR-6). Stored as a jsonb array of canonical
    /// module keys (see <see cref="HRM.Domain.Authorization.PlanModules"/>). CoreHR is always enabled.
    /// </summary>
    public List<string> EnabledModules { get; set; } = new();

    /// <summary>Per-plan feature flags (SSO, custom domain, white-label, SCIM, sandbox). Stored as jsonb (US-ADM-009 FR-2).</summary>
    public PlanFeatureFlags FeatureFlags { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the plan was last edited (US-ADM-009 AC-3). Null until first update.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Boolean feature entitlements for a subscription plan (US-ADM-009 FR-2 feature_flags). Stored as a jsonb
/// object on <see cref="SubscriptionPlan.FeatureFlags"/>. New flags can be added additively.
/// </summary>
public sealed class PlanFeatureFlags
{
    /// <summary>Single sign-on (SAML/OIDC).</summary>
    public bool Sso { get; set; }

    /// <summary>Tenant custom domain.</summary>
    public bool CustomDomain { get; set; }

    /// <summary>White-label branding.</summary>
    public bool WhiteLabel { get; set; }

    /// <summary>SCIM user provisioning.</summary>
    public bool Scim { get; set; }

    /// <summary>Sandbox environment.</summary>
    public bool Sandbox { get; set; }
}
