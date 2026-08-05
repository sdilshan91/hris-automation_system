using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using Microsoft.Extensions.Configuration;

namespace HRM.Api.Middleware;

/// <summary>
/// Wave 5 / RLS flip prep — confines the <c>/api/v1/system/*</c> namespace to the SYSTEM (admin) context.
///
/// <para><b>The gap.</b> Nothing bound those controllers to the admin host. A platform-admin JWT
/// (<c>TenantId == Guid.Empty</c>, deliberately skipped by <see cref="TenantAccessGuardMiddleware"/>) could
/// reach <c>https://acme.yourhrm.com/api/v1/system/tenants</c> and the request would resolve tenant
/// <c>acme</c>. Today that still works, because those services use <c>IgnoreQueryFilters()</c> and the EF
/// filter is fail-open.</para>
///
/// <para><b>Why RLS turns it into a real failure.</b> Under RLS a resolved, non-system ambient routes to the
/// tenant role, where the cross-tenant admin queries return ZERO ROWS and NULL-tenant audit inserts violate the
/// strict <c>WITH CHECK</c>. The platform console would half-work — empty tables and 500s — depending on which
/// host it happened to be reached from. Binding the namespace to the system context removes the ambiguity
/// instead of teaching each controller to cope with it.</para>
///
/// <para><b>Gated on the RESOLVED context, not the raw Host header.</b> <c>TenantResolutionMiddleware</c>
/// derives the subdomain from the host OR, in local development, from the <c>X-Tenant-Subdomain</c> header so
/// no hosts-file entries are needed. Reading <c>Host</c> directly here would pass in production and reject
/// every admin request on a developer's machine.</para>
///
/// <para><b>Why this ELEVATES rather than simply rejecting non-system context.</b> Platform administrators are
/// not on <c>admin.*</c> — they are users of a real tenant whose subdomain is <c>platform</c>
/// (<c>DbInitializer</c>'s default admin tenant). <c>admin.*</c> resolves to system context with
/// <c>TenantId == Guid.Empty</c>, which no <c>user_tenants</c> row can match, so nobody can log in there at
/// all. A guard that demanded <c>IsSystemContext</c> would therefore 403 the entire platform console. Instead:
/// the platform tenant is ELEVATED to system context (so connection routing picks the privileged role and the
/// cross-tenant admin queries work under RLS), and every OTHER tenant host is refused.</para>
///
/// <para><b>Elevation is not authorization.</b> It only changes which database connection the request uses.
/// A non-admin user of the platform tenant is elevated here and then still rejected by the
/// <c>[RequirePermission]</c> policy on the controller, exactly as before.</para>
/// </summary>
public sealed class SystemEndpointHostGuardMiddleware
{
    private const string SystemPathPrefix = "/api/v1/system";

    /// <summary>Fallback when <c>Platform:AdminSubdomain</c> is unset — matches DbInitializer's default.</summary>
    private const string DefaultAdminSubdomain = "platform";

    private readonly RequestDelegate _next;
    private readonly ILogger<SystemEndpointHostGuardMiddleware> _logger;
    private readonly string _adminSubdomain;

    public SystemEndpointHostGuardMiddleware(
        RequestDelegate next,
        ILogger<SystemEndpointHostGuardMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _adminSubdomain = configuration["Platform:AdminSubdomain"] is { Length: > 0 } configured
            ? configured
            : DefaultAdminSubdomain;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (context.Request.Path.StartsWithSegments(SystemPathPrefix, StringComparison.OrdinalIgnoreCase)
            && !tenantContext.IsSystemContext)
        {
            // The platform tenant IS the admin context for these endpoints — elevate so the request routes to
            // the privileged connection instead of the tenant role (where every cross-tenant admin query would
            // return zero rows under RLS).
            if (tenantContext.IsResolved
                && string.Equals(tenantContext.Subdomain, _adminSubdomain, StringComparison.OrdinalIgnoreCase))
            {
                tenantContext.SetSystemContext();
                await _next(context);
                return;
            }

            _logger.LogWarning(
                "System endpoint blocked off the admin host: {Method} {Path} resolved tenant {TenantId} "
                + "(subdomain {Subdomain}); /api/v1/system/* is admin-context only.",
                context.Request.Method, context.Request.Path,
                tenantContext.IsResolved ? tenantContext.TenantId : Guid.Empty,
                tenantContext.Subdomain);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Fail(
                    "Platform administration endpoints are only available on the admin host.",
                    "system_endpoint_requires_admin_host"));
            return;
        }

        await _next(context);
    }
}
