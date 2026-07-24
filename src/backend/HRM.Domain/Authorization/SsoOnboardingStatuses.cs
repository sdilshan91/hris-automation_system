namespace HRM.Domain.Authorization;

/// <summary>
/// US-AUTH-016 FR-5/FR-6: the valid values for <see cref="HRM.Domain.Entities.Tenant.SsoOnboardingStatus"/>.
/// Stored as a plain string on the tenant (mirrors the <see cref="SsoEnforcementModes"/> convention).
///
/// <para>Progression: <c>not_started</c> → (admin starts consent) <c>consent_pending</c> → (Microsoft admin
/// consent returns successfully, the customer <c>tid</c> is captured) <c>consented</c> → (admin explicitly
/// enables SSO — BR-3) <c>enabled</c>. Consent alone never enables SSO.</para>
/// </summary>
public static class SsoOnboardingStatuses
{
    /// <summary>No onboarding has begun (the default).</summary>
    public const string NotStarted = "not_started";

    /// <summary>The admin-consent flow has been started; awaiting the Microsoft return.</summary>
    public const string ConsentPending = "consent_pending";

    /// <summary>Admin consent completed and the customer directory id was captured; SSO is ready to enable (BR-3).</summary>
    public const string Consented = "consented";

    /// <summary>SSO has been explicitly enabled by the tenant admin.</summary>
    public const string Enabled = "enabled";

    public static readonly IReadOnlyList<string> All = new[] { NotStarted, ConsentPending, Consented, Enabled };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
