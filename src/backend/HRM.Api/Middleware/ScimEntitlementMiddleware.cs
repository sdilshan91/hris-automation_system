using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;

namespace HRM.Api.Middleware;

/// <summary>
/// D3 (ISSUE-358): the per-request <b>SCIM feature gate</b>, pre-registered ahead of the feature. The reserved
/// route prefix <c>/scim/v2</c> is gated on the tenant's plan <see cref="PlanFeatureFlagKeys.Scim"/> flag —
/// carried on <see cref="ITenantContext.FeatureFlags"/> at resolution time, so this costs no extra query.
///
/// <para>Feature flags are a DIFFERENT mechanism from module entitlements (<see cref="PlanModules"/>): a flag is
/// a boolean opt-in capability, not a toggleable module, so this is a distinct gate rather than a
/// <c>ModuleEntitlementMiddleware</c> route→module entry. There is NO SCIM controller yet — that is the point:
/// the module was SELLABLE in the plan editor while nothing enforced it (the ISSUE-356 class), so landing the
/// gate first means SCIM is enforced the moment its routes appear, instead of shipping unenforced.</para>
///
/// <para><b>Positive-list, fail-open by construction</b> (mirrors <c>ModuleEntitlementMiddleware</c>'s ethos,
/// ISSUE-335): only the <c>/scim/v2</c> prefix can be denied — every other path passes untouched — and the
/// entitlement check itself fails open (a null/unreadable flag set never denies). Anonymous-safe: only
/// <see cref="ITenantContext"/> is read, never <see cref="ICurrentUser"/>.</para>
/// </summary>
public sealed class ScimEntitlementMiddleware
{
    private const string ScimPrefix = "/scim/v2";

    private readonly RequestDelegate _next;
    private readonly ILogger<ScimEntitlementMiddleware> _logger;

    public ScimEntitlementMiddleware(RequestDelegate next, ILogger<ScimEntitlementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();

        // Only enforce for a resolved, non-system tenant (identical short-circuit to ModuleEntitlement).
        if (!tenantContext.IsResolved || tenantContext.IsSystemContext)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // Positive-list / fail-open-on-unmapped: only the reserved SCIM prefix can be denied; all else passes.
        if (!MatchesSegment(path, ScimPrefix))
        {
            await _next(context);
            return;
        }

        // Fail-open on the entitlement: a null (unreadable) flag set never denies (PlanFeatureFlagKeys.IsFeatureEnabled).
        if (PlanFeatureFlagKeys.IsFeatureEnabled(tenantContext.FeatureFlags, PlanFeatureFlagKeys.Scim))
        {
            await _next(context);
            return;
        }

        _logger.LogInformation(
            "Blocked SCIM request for tenant {TenantId} ({Path}) with HTTP 403: plan lacks the Scim feature.",
            tenantContext.TenantId, path);
        await EntitlementResponse.WriteForbiddenAsync(context,
            "SCIM provisioning is not included in your organization's current plan.", "feature_not_entitled");
    }

    // Segment-aware prefix match (same rule as ModuleEntitlementMiddleware): "/scim/v2" matches "/scim/v2" and
    // "/scim/v2/Users" but NOT a hypothetical "/scim/v2x". Raw StartsWith would conflate sibling prefixes.
    private static bool MatchesSegment(string path, string prefix)
        => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
