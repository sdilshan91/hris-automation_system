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

    /// <summary>
    /// US-CHR-009 BR-6 (ISSUE-304): the tenant's probation period in days from an employee's date of joining.
    /// Default 90 — the value that was previously HARDCODED in <c>EmployeeStatusService</c>, so an existing
    /// tenant that has never configured this behaves exactly as before.
    ///
    /// <para>A Location may override it (<see cref="Location.ProbationPeriodDays"/>); the effective period is
    /// <c>Location.ProbationPeriodDays ?? Tenant.ProbationPeriodDays</c>. Drives the probation-end reminder and,
    /// transitively, probation-gated leave eligibility timing.</para>
    /// </summary>
    public int ProbationPeriodDays { get; set; } = 90;

    /// <summary>
    /// US-LV-010 FR-7 (DF-20/ISSUE-044): the tenant's leave-cancellation notice window in days — an employee may
    /// self-cancel an APPROVED leave only when its StartDate is MORE THAN this many days after today
    /// (<c>StartDate &gt; today + N</c>). N is a before-start minimum-notice cutoff; default 0 = cancellable strictly
    /// before start, the value previously HARDCODED in <c>LeaveRequestService</c>, so a tenant that has never
    /// configured this behaves exactly as before. Applies only to Approved requests (Pending is always cancellable).
    /// Settable via the tenant-settings (org-profile) surface.
    /// </summary>
    public int LeaveCancellationWindowDays { get; set; } = 0;

    /// <summary>
    /// Default ISO country code (alpha-2/alpha-3, max 5), e.g. "LK" (settings key org.default_country_code).
    /// Multi-country tax foundation: the FALLBACK tax country used when an employee's branch/location has no
    /// <c>CountryCode</c>. When this is also null and no location country resolves, the employee's statutory
    /// deductions are SKIPPED and the employee is flagged on the payroll run (never taxed under the wrong/no
    /// country). Optional; null preserves the single-country behaviour.
    /// </summary>
    public string? DefaultCountryCode { get; set; }

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

    // ── Enterprise SSO (Microsoft Entra ID) — US-AUTH-012 FR-1 ────────────────
    // Per-tenant SSO configuration. Gated on the plan's PlanFeatureFlags.Sso (US-ADM-009). SSO is DISABLED by
    // default for every tenant (BR-1); enabling it is an explicit, audited admin action that cannot be done with
    // an empty allow-list (BR-3, fail-closed). These are the single source of truth consumed by US-AUTH-013
    // (isolation) and US-AUTH-014 (matching/JIT). Client secrets/certs are PLATFORM-level, NOT stored here.

    /// <summary>US-AUTH-012 FR-1/BR-1: master toggle for Entra SSO. Default false (disabled).</summary>
    public bool SsoEnabled { get; set; }

    /// <summary>
    /// US-AUTH-012 FR-1: trusted Entra directory (tenant) IDs — the token <c>tid</c> allow-list (jsonb). A login
    /// is admitted only when the id_token's <c>tid</c> is in this set OR its verified email domain is in
    /// <see cref="AllowedEmailDomains"/> (fail-closed). Multiple directories are supported (CR-AUTH-001 OQ-4).
    /// Stored as strings; each is validated as a well-formed GUID on write.
    /// </summary>
    public List<string> AllowedEntraTenantIds { get; set; } = new();

    /// <summary>
    /// US-AUTH-012 FR-1: verified email domains that this tenant trusts for SSO (jsonb). Used both as an admit
    /// rule and (with <see cref="JitEnabled"/>) as the gate for just-in-time provisioning. Each entry is validated
    /// as a syntactically valid domain on write.
    /// </summary>
    public List<string> AllowedEmailDomains { get; set; } = new();

    /// <summary>US-AUTH-012 FR-1: opt-in just-in-time provisioning for allow-listed users. Default false.</summary>
    public bool JitEnabled { get; set; }

    /// <summary>
    /// US-AUTH-012 FR-1/BR-5: the role assigned to JIT-provisioned users. Must be an existing tenant role and must
    /// NOT be a privileged admin/owner role (privilege-escalation guard). Null until configured.
    /// </summary>
    public string? JitDefaultRole { get; set; }

    /// <summary>
    /// US-AUTH-012 FR-1/BR-6: sign-in enforcement mode — "optional" (SSO alongside local login) or "sso_only"
    /// (SSO enforced for new logins). Default "optional". "sso_only" is accepted only when a local break-glass
    /// admin path is preserved (AC-7, US-AUTH-016) so a tenant can never lock itself out.
    /// </summary>
    public string SsoEnforcementMode { get; set; } = "optional";

    /// <summary>
    /// US-AUTH-016 FR-1/FR-2 (BR-1/BR-2): the explicitly-designated break-glass administrators — the user ids
    /// (stringified GUIDs, jsonb) allowed to ALWAYS authenticate with local credentials via the distinct
    /// break-glass login path, even under <c>sso_only</c> enforcement, so a tenant can never lock itself out.
    /// At least one valid designation is mandatory before <c>sso_only</c> can be enabled (AC-3). Empty by default.
    /// Each designated user must be an active member with a password AND an admin/owner role (validated on write).
    /// </summary>
    public List<string> BreakGlassAdminUserIds { get; set; } = new();

    /// <summary>
    /// US-AUTH-016 FR-5/FR-6: admin-consent onboarding progress — "not_started" | "consent_pending" |
    /// "consented" | "enabled" (see <see cref="HRM.Domain.Authorization.SsoOnboardingStatuses"/>). Consent alone
    /// does NOT enable SSO (BR-3): "consented" records the customer's captured Entra directory id; the admin must
    /// still explicitly enable SSO ("enabled"). Default "not_started".
    /// </summary>
    public string SsoOnboardingStatus { get; set; } = "not_started";

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
    /// Maximum number of notification-template language variants per (tenant, event) for this tenant's plan
    /// (DF-5/BR-6). Null means "use the plan value, else the historical default of 2". A dedicated column,
    /// mirroring <see cref="MaxWorkflows"/>, because it caps a different resource.
    /// </summary>
    public int? MaxTemplateLanguageVariants { get; set; }

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
    /// Tenant-level toggle (US-REC-010 FR-5 / BR-7, ISSUE-140) for auto-creating a login account when an
    /// applicant is converted to an employee. When true, the conversion provisions a passwordless
    /// <see cref="User"/> + Active <see cref="UserTenant"/> + built-in "Employee" role and links
    /// <c>Employee.UserId</c>, atomically with the conversion. Defaults to false (opt-in) so existing tenants
    /// keep the prior behaviour. Credential DELIVERY (welcome email) and the onboarding trigger remain deferred
    /// (US-NTF-006). Mirrors the plain-boolean tenant flags above (<see cref="PublicCareersEnabled"/>).
    /// </summary>
    public bool AutoCreateUserOnHire { get; set; }

    /// <summary>
    /// Tenant-level toggle for the year-to-date column on employee payslips (US-PAY-005 FR-7 / ISSUE-160). When
    /// false, the YTD earning/deduction totals are not surfaced on the self-service payslip detail; a tenant
    /// opts in. Defaults to false to preserve the prior behaviour. TODO(admin-console): surface this in tenant
    /// payroll configuration once that subsystem exists; for now a plain boolean on the tenant (mirrors
    /// <see cref="PublicCareersEnabled"/>). US-PAY-004's PDF renderer shares the same scaffold (still gated off).
    /// </summary>
    public bool PayslipYtdEnabled { get; set; }

    /// <summary>
    /// ISSUE-159 (US-PAY-004 BR-3): tenant-configurable payslip footer disclaimer. Null/blank → the renderer
    /// falls back to <see cref="HRM.Domain.Payroll.PayslipBranding.DefaultFooterDisclaimer"/> so existing
    /// tenants keep the standard wording. Settable via the tenant-settings (org-profile) surface.
    /// </summary>
    public string? PayslipFooterDisclaimer { get; set; }

    /// <summary>
    /// ISSUE-229 (US-PAY-011 BR-4): tenant-configurable sender ("From") address for payslip distribution emails.
    /// Null/blank → <see cref="HRM.Infrastructure.Services.PayslipDistributionRunner"/> resolves null and the
    /// SmtpEmailSender falls back to the system default sender, so existing tenants are unchanged. The value is
    /// NOT auto-derived from the subdomain (SPF/DKIM deliverability risk) — BR-4 requires a CONFIGURED address.
    /// Settable via the tenant-settings (org-profile) surface, validated as a well-formed email on write.
    /// </summary>
    public string? PayrollFromEmail { get; set; }

    /// <summary>
    /// Tenant-level toggle (BUG-244 Feedback360) letting DIRECT MANAGERS — not just HR — configure the 360
    /// reviewer set (add/remove Peer + Direct Report nominations) for their OWN direct reports (US-PRF-005
    /// AC-1/FR-2). HR (Performance.Review.All) is always unrestricted; when this is true a manager holding
    /// Performance.Review.Team may also configure reviewers for an employee who reports directly to them.
    /// Defaults to true (managers allowed — opt-out), mirroring the plain-boolean tenant flags above
    /// (<see cref="PublicCareersEnabled"/>/<see cref="PayslipYtdEnabled"/>). TODO(admin-console): surface this
    /// in tenant performance configuration once that subsystem exists; for now a plain boolean on the tenant.
    /// </summary>
    public bool AllowManagerReviewerConfig { get; set; } = true;

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
    /// (they can VIEW but not change it, BR-5). Tiers per the technical doc's plan matrix and §19.13:
    /// Starter 90, Professional 365, Enterprise 2555 (7 years).
    ///
    /// <para>GAP-004: this is a DENORMALIZED SNAPSHOT of <c>SubscriptionPlan.AuditLogRetentionDays</c>, copied
    /// on provisioning (<c>TenantProvisioningService</c>) and on plan change (<c>TenantLifecycleService</c>) —
    /// the same rule as <see cref="MaxEmployees"/>. <c>AuditLogPurgeService</c> reads THIS column, so a plan
    /// value that is not copied here has no effect on what gets deleted. The 90 below is the fallback for
    /// tenants created before the tiers were seeded, not the intended value for a paying tier.</para>
    ///
    /// <para>The former <c>TODO(subscription)</c> here ("derive from the plan tier once a proper
    /// Subscription/Plan entity exists") was stale — <see cref="SubscriptionPlan"/> shipped with US-ADM-009.</para>
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
