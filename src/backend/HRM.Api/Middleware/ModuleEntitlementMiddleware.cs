using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Domain.Authorization;

namespace HRM.Api.Middleware;

/// <summary>
/// US-ADM-012 (phase 2b, AC-1): the per-request <b>module entitlement gate</b>. A tenant's subscription plan
/// enables a subset of the toggleable product modules (<see cref="PlanModules.All"/>); this middleware maps the
/// request's route prefix to its owning module and returns <c>403 module_not_entitled</c> when that module is not
/// in the tenant's <see cref="ITenantContext.EnabledModules"/> snapshot. The FE mirrors both the predicate
/// (<see cref="PlanModules.IsModuleEnabled"/>) and the <c>module_not_entitled</c> code, so the two agree.
///
/// <para>Modelled on <see cref="TenantStatusEnforcementMiddleware"/> (same shape / 403 body style) and slotted
/// immediately after it — after auth, <c>TenantAccessGuardMiddleware</c> and <c>ImpersonationEnforcementMiddleware</c>,
/// before the controllers. It is done as middleware rather than an authorization attribute/handler for two reasons:
/// an attribute cannot emit the <see cref="ApiResponse"/> envelope without a custom
/// <c>IAuthorizationMiddlewareResultHandler</c>, and gating ~60 controllers by annotation is error-prone.</para>
///
/// <para><b>Positive-list, fail-open by construction.</b> Only a path that matches a known product-module prefix
/// in <see cref="RouteModuleMap"/> can ever be denied. Everything else — non-<c>/api/</c> paths, the platform
/// allow-list, CoreHR routes (employees/departments/etc.), and any route we have not mapped — passes untouched.
/// This is deliberate and matches <see cref="PlanModules.IsModuleEnabled"/>'s own fail-open ethos (ISSUE-335): the
/// cost of a mis-mapped route is "not enforced", never "a whole tenant locked out of their own workspace". A
/// deny-by-default design would turn every new/unmapped platform endpoint into a potential outage. The explicit
/// <see cref="PlatformAllowList"/> is belt-and-suspenders: it documents the cross-cutting paths that must never be
/// gated and short-circuits them even if a future map entry accidentally overlapped one.</para>
///
/// <para><b>Anonymous-safe.</b> Only <see cref="ITenantContext"/> is read — never <see cref="ICurrentUser"/> — so
/// anonymous per-tenant traffic (the public careers/portal pages) is gated without NRE risk.</para>
/// </summary>
public sealed class ModuleEntitlementMiddleware
{
    // The cross-cutting platform allow-list (auth / tenant-admin / notifications / system) lives in the shared
    // PlatformApiPaths helper so this gate and the US-PLT-004 ApiCallCounterMiddleware skip IDENTICAL paths — a
    // path we don't meter must be a path we don't gate, and vice versa. See PlatformApiPaths.

    // Route-prefix → owning module. The prefix is NOT cleanly 1:1 with the module (see the ordered traps below),
    // so this table is enumerated from the controllers' actual [Route] attributes, not guessed from the module name.
    // ORDER MATTERS: matched first-wins, so more-specific prefixes precede the prefix they sit under
    // (onboarding/assets → Asset MUST precede onboarding → Onboarding). CoreHR routes (employees, departments,
    // job-titles, locations, org-tree, custom-fields, holidays, dashboard) are intentionally ABSENT — CoreHR is
    // always-on and must never be denied, so leaving it unmapped lets it fall open.
    private static readonly (string Prefix, string Module)[] RouteModuleMap =
    {
        // Leave splits across three prefixes (LeaveTypes/LeaveEntitlements sit under /tenant/, the rest under /leaves).
        ("/api/v1/tenant/leave-entitlements", PlanModules.Leave),
        ("/api/v1/tenant/leave-types", PlanModules.Leave),

        // Payroll's SalaryGradesController lives OUTSIDE /payroll, at /tenant/salary-grades — the headline trap.
        ("/api/v1/tenant/salary-grades", PlanModules.Payroll),

        ("/api/v1/tenant/performance", PlanModules.Performance),
        ("/api/v1/tenant/training", PlanModules.Training),
        ("/api/v1/tenant/benefits", PlanModules.Benefits),

        // Asset has no dedicated controller — it lives under OnboardingAssetsController at /onboarding/assets, so it
        // MUST be matched before the broader /onboarding (Onboarding) prefix below.
        ("/api/v1/onboarding/assets", PlanModules.Asset),
        ("/api/v1/onboarding", PlanModules.Onboarding),
        // Offboarding + exit interviews are the "off" half of the Onboarding/Offboarding lifecycle module (there is
        // no separate Offboarding module in PlanModules), so they gate under Onboarding.
        ("/api/v1/offboarding", PlanModules.Onboarding),
        ("/api/v1/exit-interviews", PlanModules.Onboarding),

        ("/api/v1/leaves", PlanModules.Leave),
        ("/api/v1/attendance", PlanModules.Attendance),
        ("/api/v1/recruitment", PlanModules.Recruitment),
        ("/api/v1/payroll", PlanModules.Payroll),
        // ISSUE-356 — CustomReportBuilder gate, PRE-REGISTERED ahead of the feature (user decision 2026-07-31).
        //
        // What this does and does not do, stated plainly so nobody mistakes it for more than it is: there is no
        // custom-report-builder feature yet, so TODAY these prefixes match no controller and the entry gates
        // nothing. Its purpose is that the module was already SELLABLE — grantable in the plan editor — while
        // nothing enforced it, so a tenant could be billed for it and denied it with identical behaviour. Landing
        // the map entry first means the builder is gated the moment its routes exist, instead of shipping ungated
        // and needing a follow-up nobody files.
        //
        // MUST precede the broader "/api/v1/reports" entry below: matching is first-wins on prefix (same reason
        // /onboarding/assets precedes /onboarding above), otherwise a custom-builder route would gate under
        // Reporting and the CustomReportBuilder entitlement would stay unenforceable.
        //
        // The pre-defined report TYPES under /api/v1/reports are Reporting, NOT CustomReportBuilder — they are a
        // different product capability and are deliberately left where they are.
        ("/api/v1/reports/custom", PlanModules.CustomReportBuilder),
        ("/api/v1/report-builder", PlanModules.CustomReportBuilder),

        ("/api/v1/reports", PlanModules.Reporting),

        // Public careers/portal (CareersController + PortalController) — anonymous, per-tenant public pages.
        ("/api/v1/careers", PlanModules.PublicCareersPage),
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ModuleEntitlementMiddleware> _logger;

    public ModuleEntitlementMiddleware(RequestDelegate next, ILogger<ModuleEntitlementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();

        // Only enforce for a resolved, non-system tenant (identical short-circuit to TenantStatusEnforcement).
        if (!tenantContext.IsResolved || tenantContext.IsSystemContext)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // Non-API paths (health/swagger/hangfire) and the platform allow-list are never gated (shared predicate).
        if (!PlatformApiPaths.IsMeteredTenantApiPath(path))
        {
            await _next(context);
            return;
        }

        var module = ResolveModule(path);

        // Unmapped route (incl. all CoreHR routes) ⇒ fall open. Only a mapped module can be denied.
        if (module is null || PlanModules.IsModuleEnabled(tenantContext.EnabledModules, module))
        {
            await _next(context);
            return;
        }

        _logger.LogInformation(
            "Blocked request to non-entitled module {Module} for tenant {TenantId} ({Path}) with HTTP 403.",
            module, tenantContext.TenantId, path);
        await WriteAsync(context, StatusCodes.Status403Forbidden,
            "This feature is not included in your organization's current plan.",
            "module_not_entitled");
    }

    private static string? ResolveModule(string path)
    {
        foreach (var (prefix, module) in RouteModuleMap)
        {
            if (MatchesSegment(path, prefix))
                return module;
        }

        return null;
    }

    // Segment-aware prefix match: "/api/v1/leaves" matches "/api/v1/leaves" and "/api/v1/leaves/123" but NOT a
    // hypothetical "/api/v1/leaves-archive". Raw StartsWith would conflate sibling prefixes.
    private static bool MatchesSegment(string path, string prefix)
        => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteAsync(HttpContext context, int statusCode, string message, string code)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(message, code));
    }
}
