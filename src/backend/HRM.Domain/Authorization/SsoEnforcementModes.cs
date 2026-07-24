namespace HRM.Domain.Authorization;

/// <summary>
/// US-AUTH-012 FR-1/BR-6: the valid values for <see cref="HRM.Domain.Entities.Tenant.SsoEnforcementMode"/>.
/// Stored as a plain string on the tenant (mirrors the MfaPolicy / ConcurrentSessionStrategy convention).
/// </summary>
public static class SsoEnforcementModes
{
    /// <summary>SSO is available alongside local password login (the default).</summary>
    public const string Optional = "optional";

    /// <summary>SSO is enforced for new logins; requires a preserved local break-glass admin (AC-7, US-AUTH-016).</summary>
    public const string SsoOnly = "sso_only";

    public static readonly IReadOnlyList<string> All = new[] { Optional, SsoOnly };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
