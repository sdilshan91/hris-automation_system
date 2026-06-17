namespace HRM.Domain.Enums;

/// <summary>
/// Role responsible for completing an onboarding template task (US-ONB-001 §7, FR-3).
/// Stored as a string column for readability/forward-compat (see OnboardingTemplateTaskConfiguration).
/// A named user can additionally/instead be specified via <c>responsible_user_id</c>.
/// </summary>
public enum OnboardingResponsibleRole
{
    /// <summary>The HR team (default responsible party for most onboarding tasks).</summary>
    HR = 0,

    /// <summary>The new hire's line manager.</summary>
    Manager = 1,

    /// <summary>The IT team (e.g. account/hardware provisioning).</summary>
    IT = 2,

    /// <summary>The new hire themselves (e.g. submit documents, complete profile).</summary>
    Employee = 3,

    /// <summary>A custom/other party; typically paired with a named responsible user.</summary>
    Custom = 4,
}
