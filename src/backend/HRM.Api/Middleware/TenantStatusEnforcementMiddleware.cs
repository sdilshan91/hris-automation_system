using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;

namespace HRM.Api.Middleware;

/// <summary>
/// US-ADM-004 (AC-1 / AC-2 / BR-6): enforces a resolved tenant's lifecycle status on every tenant API request.
/// Slotted AFTER authentication/authorization (so <see cref="ICurrentUser"/> roles are populated) and after the
/// impersonation enforcement, before the controllers.
///
/// <list type="bullet">
///   <item><b>Suspended</b> (AC-1/AC-2): returns HTTP 451 for tenant API requests, EXCEPT Tenant Owner / Tenant
///   Admin users (who reach the read-only suspension notice + data export) and the always-allowed
///   suspension-notice / data-export / auth endpoints.</item>
///   <item><b>Terminating</b> (BR-6): read-only — GET/HEAD/OPTIONS and export endpoints pass; any write
///   (POST/PUT/PATCH/DELETE) returns 403, except the always-allowed auth/export endpoints.</item>
/// </list>
///
/// <para>The system/admin context is EXEMPT (the System Admin must still operate a suspended/terminating
/// tenant). Non-API paths (health, swagger, hangfire) and unresolved/active tenants pass through untouched.</para>
/// </summary>
public sealed class TenantStatusEnforcementMiddleware
{
    private static readonly HashSet<string> ReadMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS",
    };

    // Endpoints that remain reachable even on a suspended/terminating tenant (AC-2): auth (login/refresh/logout),
    // the data export, and the suspension-notice read. Matched as path prefixes (case-insensitive).
    private static readonly string[] AlwaysAllowedPrefixes =
    {
        "/api/v1/auth",
        "/api/v1/tenant/lifecycle-notice",
    };

    // Substrings that mark an export endpoint (kept reachable during terminating read-only mode and on a
    // suspended tenant for admins — data export is the whole point of the grace period).
    private static readonly string[] ExportMarkers = { "/export", "/exports" };

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantStatusEnforcementMiddleware> _logger;

    public TenantStatusEnforcementMiddleware(
        RequestDelegate next, ILogger<TenantStatusEnforcementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();

        // Only enforce for a resolved, non-system tenant.
        if (!tenantContext.IsResolved || tenantContext.IsSystemContext)
        {
            await _next(context);
            return;
        }

        var status = tenantContext.Status;
        if (status is not (TenantStatus.Suspended or TenantStatus.Terminating))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // Non-API and always-allowed endpoints (auth/export/notice) pass through regardless of status.
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || IsAlwaysAllowed(path))
        {
            await _next(context);
            return;
        }

        var currentUser = context.RequestServices.GetRequiredService<ICurrentUser>();

        if (status == TenantStatus.Suspended)
        {
            // AC-2: only Tenant Owner / Tenant Admin may operate a suspended tenant (read-only notice + export).
            if (IsTenantAdminOrOwner(currentUser))
            {
                await _next(context);
                return;
            }

            _logger.LogInformation(
                "Blocked request to suspended tenant {TenantId} ({Path}) with HTTP 451.",
                tenantContext.TenantId, path);
            await WriteAsync(context, StatusCodes.Status451UnavailableForLegalReasons,
                "This organization's account has been suspended. Please contact your administrator.",
                "tenant_suspended");
            return;
        }

        // Terminating (BR-6): read-only. GET/HEAD/OPTIONS + export pass; writes are 403.
        if (ReadMethods.Contains(context.Request.Method) || IsExport(path))
        {
            await _next(context);
            return;
        }

        _logger.LogInformation(
            "Blocked write to terminating tenant {TenantId} ({Method} {Path}) with HTTP 403.",
            tenantContext.TenantId, context.Request.Method, path);
        await WriteAsync(context, StatusCodes.Status403Forbidden,
            "This organization's workspace is read-only pending termination. Write operations are disabled.",
            "tenant_terminating_readonly");
    }

    private static bool IsAlwaysAllowed(string path)
        => AlwaysAllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
           || IsExport(path);

    private static bool IsExport(string path)
        => ExportMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static bool IsTenantAdminOrOwner(ICurrentUser user)
        => user.IsAuthenticated
           && (user.Roles.Contains(PermissionCatalog.BuiltInRoles.TenantOwner)
               || user.Roles.Contains(PermissionCatalog.BuiltInRoles.TenantAdmin));

    private static async Task WriteAsync(HttpContext context, int statusCode, string message, string code)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(message, code));
    }
}
