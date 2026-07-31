using System.Linq;
using HRM.Domain.Entities;

namespace HRM.Domain.Authorization;

/// <summary>
/// D3 (ISSUE-358): the canonical string keys for the per-plan <see cref="PlanFeatureFlags"/> entitlements
/// (SSO, CustomDomain, WhiteLabel, SCIM, Sandbox) plus the ONE place that derives a tenant's enabled-flag
/// snapshot from a plan and answers "is this flag entitled?". Feature flags are a DIFFERENT mechanism from
/// <see cref="PlanModules"/> (module entitlements): a flag is a boolean opt-in capability, not a toggleable
/// module, so it is NOT gated through <c>ModuleEntitlementMiddleware</c>. Every seam (the SCIM route gate,
/// the custom-domain resolution gate, the sandbox provisioning gate) shares this derivation + predicate so
/// their semantics cannot drift, exactly as <see cref="PlanModules.IsModuleEnabled"/> does for modules.
/// </summary>
public static class PlanFeatureFlagKeys
{
    public const string Sso = "Sso";
    public const string CustomDomain = "CustomDomain";
    public const string WhiteLabel = "WhiteLabel";
    public const string Scim = "Scim";
    public const string Sandbox = "Sandbox";

    /// <summary>All recognized feature-flag keys.</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Sso, CustomDomain, WhiteLabel, Scim, Sandbox };

    /// <summary>
    /// Derive the ENABLED-flag set for a plan. Returns <c>null</c> when no flags object is available (no plan
    /// row / unreadable config) — the FAIL-OPEN sentinel the gates key off, so a config problem is "not
    /// enforced", never "customer locked out". A resolved flags object (even one with every flag false) yields
    /// a non-null, AUTHORITATIVE set: absence of a flag then genuinely means "not entitled", so a seam can deny.
    /// </summary>
    public static IReadOnlyCollection<string>? Derive(PlanFeatureFlags? flags)
    {
        if (flags is null)
            return null;

        var set = new List<string>(All.Count);
        if (flags.Sso) set.Add(Sso);
        if (flags.CustomDomain) set.Add(CustomDomain);
        if (flags.WhiteLabel) set.Add(WhiteLabel);
        if (flags.Scim) set.Add(Scim);
        if (flags.Sandbox) set.Add(Sandbox);
        return set;
    }

    /// <summary>
    /// Is <paramref name="flag"/> entitled given the tenant's resolved <paramref name="flags"/> set? FAIL-OPEN
    /// by design: a <c>null</c> set (the plan could not be resolved) returns <c>true</c> — never lock a tenant
    /// out for a config problem. A non-null set is authoritative and returns <c>true</c> only when it contains
    /// the flag. Mirrors <see cref="PlanModules.IsModuleEnabled"/>(null, …) ⇒ true (ISSUE-335 ethos).
    /// </summary>
    public static bool IsFeatureEnabled(IReadOnlyCollection<string>? flags, string flag)
        => flags is null || flags.Contains(flag);
}
