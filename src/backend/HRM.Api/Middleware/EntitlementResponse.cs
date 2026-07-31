using HRM.Application.DTOs;

namespace HRM.Api.Middleware;

/// <summary>
/// D3 (ISSUE-358): the ONE place the entitlement seams emit their 403 refusal, so the SCIM route gate and the
/// custom-domain resolution gate return an IDENTICAL machine-readable <see cref="ApiResponse"/> envelope (same
/// shape as <c>ModuleEntitlementMiddleware</c>'s <c>module_not_entitled</c>).
/// </summary>
internal static class EntitlementResponse
{
    public static async Task WriteForbiddenAsync(HttpContext context, string message, string code)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(message, code));
    }
}
