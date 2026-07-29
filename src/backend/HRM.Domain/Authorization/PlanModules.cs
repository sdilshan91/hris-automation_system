namespace HRM.Domain.Authorization;

/// <summary>
/// The canonical list of toggleable product modules a subscription plan may enable (US-ADM-009 FR-6).
/// CoreHR is ALWAYS enabled and cannot be turned off. The plan editor validates <c>enabled_modules</c>
/// against this list and rejects unknown module keys.
/// </summary>
public static class PlanModules
{
    public const string CoreHr = "CoreHR";
    public const string Leave = "Leave";
    public const string Attendance = "Attendance";
    public const string Recruitment = "Recruitment";
    public const string Onboarding = "Onboarding";
    public const string Payroll = "Payroll";
    public const string Performance = "Performance";
    public const string Training = "Training";
    public const string Asset = "Asset";
    public const string Benefits = "Benefits";
    public const string Reporting = "Reporting";
    public const string CustomReportBuilder = "CustomReportBuilder";
    public const string PublicCareersPage = "PublicCareersPage";

    /// <summary>The full canonical module list, in display order. CoreHR is first and always-on.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        CoreHr, Leave, Attendance, Recruitment, Onboarding, Payroll,
        Performance, Training, Asset, Benefits, Reporting, CustomReportBuilder, PublicCareersPage,
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    /// <summary>True iff <paramref name="module"/> is a recognized canonical module key.</summary>
    public static bool IsValid(string module) => Set.Contains(module);

    /// <summary>
    /// The ONE place that answers "is <paramref name="module"/> enabled for a tenant holding
    /// <paramref name="tenantModules"/>?" — used by every entitlement gate so the semantics cannot drift
    /// between call sites (US-ADM-012 AC-1).
    ///
    /// <para><b>FAIL-OPEN by design (ISSUE-335).</b> If <paramref name="tenantModules"/> contains <b>any</b>
    /// token that is not a canonical module key, the whole list is treated as an untrusted vocabulary and this
    /// returns <c>true</c> — the tenant is fully entitled. The reason is a real incident class this repo hit:
    /// <c>enabled_modules</c> was for a long time seeded from <c>PermissionCatalog.ByModule.Keys</c> (permission
    /// prefixes such as <c>Audit</c>/<c>CustomField</c>/<c>Roles</c>) rather than from <see cref="All"/>. Nothing
    /// read the column, so the drift was invisible. A gate that failed CLOSED on that data would have denied
    /// <b>every</b> request for those tenants — including <c>CoreHR</c>, which covers employees, departments and
    /// the dashboard — turning a silent data problem into a total outage on deploy.</para>
    ///
    /// <para><b>Why "any unrecognized token" and not "no recognized tokens".</b> The obvious formulation — fail
    /// open only when nothing at all is recognized — is useless against the very data it was written for: the
    /// legacy permission vocabulary <i>overlaps</i> the canonical one (<c>Leave</c>, <c>Payroll</c>,
    /// <c>Attendance</c>, <c>Performance</c>… appear in both). It therefore looks "recognized", the backstop
    /// never engages, and <c>CoreHR</c> — absent from the legacy list — is still denied. That formulation was
    /// written first and caught by TC-ADM-012-03 before it shipped. The correct rule is the stricter one: a list
    /// containing tokens we do not understand is a list we do not understand.</para>
    ///
    /// <para>This is a <b>backstop</b>, not the fix. The fix is the normalization migration
    /// (<c>Platform_NormalizeTenantEnabledModules</c>) plus seeding from <see cref="All"/>, kept honest by the
    /// drift guard test. This exists so the NEXT drift degrades to "not enforced" rather than "nobody can log
    /// in". A legitimately restricted plan is unaffected: the plan editor validates every module against
    /// <see cref="All"/>, so a real restriction contains only canonical keys and stays fully authoritative.</para>
    ///
    /// <para>An empty/absent list is likewise fully entitled: provisioning already falls back to the full module
    /// set when a plan lists none, so "no modules recorded" has always meant "not restricted".</para>
    /// </summary>
    public static bool IsModuleEnabled(IEnumerable<string>? tenantModules, string module)
    {
        if (tenantModules is null)
            return true;

        var any = false;
        var granted = false;

        foreach (var stored in tenantModules)
        {
            any = true;

            // One unrecognized token condemns the whole list: we cannot tell a restriction from a drifted
            // vocabulary, and guessing wrong in the closed direction is an outage. See remarks.
            if (!Set.Contains(stored))
                return true;

            if (string.Equals(stored, module, StringComparison.Ordinal))
                granted = true;
        }

        return any ? granted : true; // empty list ⇒ unrestricted
    }
}

/// <summary>
/// Canonical keys for the per-tenant plan-limit overrides (US-ADM-009 FR-4). These are the keys a
/// <c>PlanLimitOverride.LimitKey</c> may take and the keys callers pass to <c>PlanLimitResolver.Resolve</c>.
/// </summary>
public static class PlanLimitKeys
{
    public const string MaxEmployees = "max_employees";
    public const string MaxStorageGb = "max_storage_gb";
    public const string MaxApiCallsPerMonth = "max_api_calls_per_month";
    public const string MaxEmailSendsPerMonth = "max_email_sends_per_month";
    public const string MaxCustomRoles = "max_custom_roles";
    public const string MaxCustomFieldsPerEntity = "max_custom_fields_per_entity";
    public const string MaxWorkflows = "max_workflows";
    public const string MaxTemplateLanguageVariants = "max_template_language_variants";

    /// <summary>All recognized limit keys.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        MaxEmployees, MaxStorageGb, MaxApiCallsPerMonth, MaxEmailSendsPerMonth,
        MaxCustomRoles, MaxCustomFieldsPerEntity, MaxWorkflows, MaxTemplateLanguageVariants,
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    /// <summary>True iff <paramref name="key"/> is a recognized limit key.</summary>
    public static bool IsValid(string key) => Set.Contains(key);
}
