namespace HRM.Application.Common.Security;

/// <summary>
/// US-ADM-006 BR-3: a minimal subscription-plan gate. Certain settings (e.g. custom CSS / white-label) are
/// enterprise-only and must be rejected with a clear "Upgrade your plan" message on non-enterprise plans.
///
/// <para>The gated settings themselves (custom CSS, login-page customization) are Phase-2 deferred per the
/// story's §10 Assumptions, so today nothing wired in this US is plan-gated. This helper exists so the moment
/// such a setting is exposed it can call <see cref="EnsureAllowed"/> with the current plan, and it is unit
/// tested to prove the gate works. A plan is treated as "enterprise" by name match (case-insensitive).</para>
/// </summary>
public static class TenantPlanGate
{
    public const string UpgradeMessage = "This is an enterprise-only setting. Upgrade your plan to enable it.";

    /// <summary>True when <paramref name="planId"/> denotes an enterprise tier.</summary>
    public static bool IsEnterprise(string? planId)
        => !string.IsNullOrWhiteSpace(planId)
           && planId.Contains("enterprise", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns null when an enterprise-only setting is permitted for <paramref name="planId"/>, otherwise the
    /// "upgrade your plan" error message. Callers return a 403 with this message when it is non-null.
    /// </summary>
    public static string? EnsureEnterprise(string? planId)
        => IsEnterprise(planId) ? null : UpgradeMessage;
}
