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

    /// <summary>All recognized limit keys.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        MaxEmployees, MaxStorageGb, MaxApiCallsPerMonth, MaxEmailSendsPerMonth,
        MaxCustomRoles, MaxCustomFieldsPerEntity, MaxWorkflows,
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    /// <summary>True iff <paramref name="key"/> is a recognized limit key.</summary>
    public static bool IsValid(string key) => Set.Contains(key);
}
