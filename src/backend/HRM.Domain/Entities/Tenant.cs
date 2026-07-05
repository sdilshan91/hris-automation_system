namespace HRM.Domain.Entities;

/// <summary>
/// Represents an organization (tenant) in the multi-tenant HRM platform.
/// </summary>
public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Subdomain { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public string PlanId { get; set; } = "default";
    public List<string> EnabledModules { get; set; } = new();
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? ContactEmail { get; set; }

    // ── Organization profile (US-ADM-006 AC-1/FR-1) ──────────────────────────
    // Name already exists above; these are the remaining org.* settings keys realized as typed columns.

    /// <summary>Registered legal/trading name (settings key org.legal_name). US-ADM-006 AC-1.</summary>
    public string? LegalName { get; set; }

    /// <summary>Company registration / incorporation number (settings key org.registration_number).</summary>
    public string? RegistrationNumber { get; set; }

    /// <summary>Free-form (or JSON) registered address (settings key org.address).</summary>
    public string? Address { get; set; }

    /// <summary>Industry/sector label (settings key org.industry).</summary>
    public string? Industry { get; set; }

    /// <summary>Company-size band, e.g. "1-10", "11-50" (settings key org.size).</summary>
    public string? CompanySize { get; set; }

    /// <summary>
    /// Fiscal-year start month, 1-12 (settings key org.fiscal_year_start). Default 1 (January). BR-4: drives
    /// leave accrual, payroll cycles, and reporting periods.
    /// </summary>
    public int FiscalYearStartMonth { get; set; } = 1;

    // ── Localization defaults (US-ADM-006 AC-3/FR-4, BR-5) ────────────────────

    /// <summary>Default UI language code (settings key locale.default_language). BR-5: applies to users with no personal preference.</summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>Default date-display format token (settings key locale.date_format), e.g. "dd MMM yyyy".</summary>
    public string DateFormat { get; set; } = "dd MMM yyyy";

    /// <summary>Default number format token (settings key locale.number_format), e.g. "1,234.56".</summary>
    public string NumberFormat { get; set; } = "1,234.56";

    /// <summary>Default IANA/Windows time-zone id (settings key locale.time_zone).</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Default ISO-4217 currency code (settings key locale.currency).</summary>
    public string Currency { get; set; } = "USD";

    // ── Branding extras (US-ADM-006 AC-2/FR-2) ────────────────────────────────
    // LogoUrl + PrimaryColor already exist above.

    /// <summary>URL of the email-header logo (settings key branding.email_logo_url).</summary>
    public string? EmailLogoUrl { get; set; }

    /// <summary>URL of the browser favicon (settings key branding.favicon_url).</summary>
    public string? FaviconUrl { get; set; }

    /// <summary>
    /// When the trial period ends (US-ADM-001 BR-3). Set at provisioning when the chosen plan has
    /// TrialDays &gt; 0; null for tenants created directly in Active status (TrialDays = 0).
    /// </summary>
    public DateTime? TrialEndsAt { get; set; }

    /// <summary>
    /// Billing contact email (US-ADM-001 BR-4). Defaults to the primary owner email at provisioning.
    /// Billing/payment is offline in Phase 1; this is informational.
    /// </summary>
    public string? BillingEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    // ── Lifecycle: suspend / terminate (US-ADM-004) ──────────────────────────

    /// <summary>
    /// When the tenant was suspended (US-ADM-004 AC-1/FR-1). Set on Suspend, cleared on Reactivate. Null when
    /// the tenant is not (and has never been) suspended.
    /// </summary>
    public DateTime? SuspendedAt { get; set; }

    /// <summary>
    /// The System-Admin-supplied reason a tenant was suspended (US-ADM-004 AC-1/FR-1, 10-500 chars). Surfaced
    /// on the read-only suspension notice (AC-2). Cleared on Reactivate.
    /// </summary>
    public string? SuspendedReason { get; set; }

    /// <summary>
    /// When the scheduled hard-deletion fires for a tenant in <see cref="TenantStatus.Terminating"/>
    /// (US-ADM-004 AC-3/FR-2) — set to now + grace period on Terminate, cleared on Restore. Null otherwise.
    /// </summary>
    public DateTime? TerminationScheduledAt { get; set; }

    // Session policy settings
    public int MaxConcurrentSessions { get; set; } = 5;
    public string ConcurrentSessionStrategy { get; set; } = "revoke_oldest"; // "deny_new" | "revoke_oldest"
    public int IdleTimeoutMinutes { get; set; } = 60;
    public int AbsoluteTimeoutHours { get; set; } = 24;

    // Lockout policy settings
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
    public bool ProgressiveLockoutEnabled { get; set; }

    // MFA policy
    public string MfaPolicy { get; set; } = "off"; // "off" | "optional" | "required"
    public List<string> MfaRequiredRoles { get; set; } = new(); // jsonb: roles that require MFA when policy is "required"

    /// <summary>
    /// Maximum number of employees allowed for this tenant's subscription plan (FR-5).
    /// Null means unlimited. TODO(subscription): move to a proper Subscription/Plan entity.
    /// </summary>
    public int? MaxEmployees { get; set; }

    /// <summary>
    /// Maximum number of custom fields per entity type for this tenant (US-CHR-012 FR-6).
    /// Null means use the default (20). TODO(subscription): replace with plan-tier lookup
    /// (Starter=5, Professional=20, Enterprise=unlimited) when the Subscription module exists.
    /// </summary>
    public int? MaxCustomFields { get; set; }

    /// <summary>
    /// Maximum number of ACTIVE (non-archived) approval-workflow definitions allowed for this tenant's plan
    /// (US-ADM-007 FR-4/AC-4). Null means unlimited. A dedicated plan-limit column was added (mirroring
    /// <see cref="MaxEmployees"/>/<see cref="MaxCustomFields"/>) rather than reusing those, because they cap a
    /// different resource. TODO(subscription): move all Max* limits to a proper Subscription/Plan entity.
    /// </summary>
    public int? MaxWorkflows { get; set; }

    /// <summary>
    /// Number of days a goal may go without a progress update before the daily stale-goal sweep nudges the
    /// employee and flags the goal "Needs Attention" for the manager (US-PRF-009 AC-5/FR-6/BR-4). Default 14.
    /// Setting it to 0 DISABLES nudge notifications for the tenant (BR-4). TODO(admin-console): surface this in
    /// tenant performance configuration once that subsystem exists; for now it is a plain int on the tenant.
    /// </summary>
    public int StaleGoalNudgeDays { get; set; } = 14;

    /// <summary>
    /// Tenant-level toggle for the public careers page (US-REC-001 FR-4 / BR-5, ref S35.2.9). When
    /// false, Open vacancies are never exposed on the anonymous public endpoint regardless of a
    /// vacancy's own PublishToPublicCareers flag. Defaults to false (opt-in). TODO(admin-console):
    /// surface this in tenant module configuration once that subsystem exists; for now it is a plain
    /// boolean on the tenant.
    /// </summary>
    public bool PublicCareersEnabled { get; set; }

    /// <summary>
    /// Tenant-level toggle for the year-to-date column on employee payslips (US-PAY-005 FR-7 / ISSUE-160). When
    /// false, the YTD earning/deduction totals are not surfaced on the self-service payslip detail; a tenant
    /// opts in. Defaults to false to preserve the prior behaviour. TODO(admin-console): surface this in tenant
    /// payroll configuration once that subsystem exists; for now a plain boolean on the tenant (mirrors
    /// <see cref="PublicCareersEnabled"/>). US-PAY-004's PDF renderer shares the same scaffold (still gated off).
    /// </summary>
    public bool PayslipYtdEnabled { get; set; }

    // Password policy
    public int MinPasswordLength { get; set; } = 12;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireDigit { get; set; } = true;
    public bool RequireSpecialCharacter { get; set; } = true;
    public int PasswordHistoryCount { get; set; } = 5;

    /// <summary>
    /// Maximum password age in days before a change is required (US-ADM-006 AC-4 / FR-5, settings key
    /// security.password_policy.max_age). Null means passwords never expire. Enforcement of expiry at next
    /// change is owned by the auth/onboarding flow; this column stores the configured policy.
    /// </summary>
    public int? PasswordMaxAgeDays { get; set; }

    /// <summary>
    /// US-ADM-008 (FR-6/BR-5): number of days audit-log rows are retained before the
    /// <c>AuditLogPurgeJob</c> deletes them. PLAN-GOVERNED — surfaced READ-ONLY to the Tenant Admin
    /// (they can VIEW but not change it, BR-5). Default 90 (Starter plan); Business=365, Enterprise=2555 etc.
    /// TODO(subscription): derive from the plan tier once a proper Subscription/Plan entity exists; for now a
    /// plain int on the tenant mirroring the other Max*/policy columns.
    /// </summary>
    public int AuditLogRetentionDays { get; set; } = 90;

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
}

public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Suspended = 3,
    Terminating = 4,
    Terminated = 5
}
