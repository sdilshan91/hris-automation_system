using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;

namespace HRM.Api.Middleware;

/// <summary>
/// BUG-003: enforces that an authenticated request's JWT tenant matches the tenant resolved from the
/// request host/subdomain. Slotted AFTER Authentication/Authorization (so <see cref="ICurrentUser"/> is
/// populated) and before the controllers.
///
/// <para>The dev <c>X-Tenant-Subdomain</c> header (and the production subdomain) drive
/// <see cref="ITenantContext"/> in <see cref="TenantResolutionMiddleware"/>, which runs before auth. On
/// its own that is spoofable: a user holding a token for tenant A could point the header/subdomain at
/// tenant B and every tenant-scoped query would run against B. This guard closes that hole by rejecting
/// (403) any authenticated request whose token tenant differs from the resolved tenant.</para>
///
/// <para>It deliberately does NOT touch tenant RESOLUTION — the dev header fallback still works; a normal
/// request (token A + subdomain/header A) matches and passes. It skips: unauthenticated requests, the
/// system/admin context, requests where no tenant was resolved, and tokens that carry no business tenant
/// (e.g. platform-admin tokens) so those flows are unaffected.</para>
/// </summary>
public sealed class TenantAccessGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantAccessGuardMiddleware> _logger;

    public TenantAccessGuardMiddleware(
        RequestDelegate next, ILogger<TenantAccessGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, ITenantContext tenantContext)
    {
        // Only enforce for an authenticated request whose token carries a business tenant, against a
        // resolved (non-system) tenant. Everything else is untouched.
        if (currentUser.IsAuthenticated
            && currentUser.TenantId != Guid.Empty
            && tenantContext.IsResolved
            && !tenantContext.IsSystemContext
            && currentUser.TenantId != tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Cross-tenant access blocked (BUG-003): token tenant {TokenTenant} != resolved tenant "
                + "{ResolvedTenant} for {Method} {Path}",
                currentUser.TenantId, tenantContext.TenantId, context.Request.Method, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Fail("Tenant access denied.", "cross_tenant_denied"));
            return;
        }

        await _next(context);
    }
}
