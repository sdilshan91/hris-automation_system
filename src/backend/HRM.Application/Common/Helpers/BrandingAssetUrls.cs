namespace HRM.Application.Common.Helpers;

/// <summary>
/// Bridges the two representations of a tenant branding asset (ISSUE-204):
///
/// <list type="bullet">
/// <item><b>Stored form</b> — what <see cref="Interfaces.IFileStorage.UploadAsync"/> returns and
/// <c>Tenant.LogoUrl</c>/<c>EmailLogoUrl</c> persists: the INTERNAL storage path
/// <c>/{tenantId}/branding/{file}</c>. This is what backend renderers (e.g. PayslipBatchRenderer) read bytes
/// from and must stay unchanged.</item>
/// <item><b>Servable form</b> — what the FE renders in an <c>&lt;img src&gt;</c>: an absolute URL to the public
/// <c>GET /api/v1/tenant/settings/branding/logo</c> serving endpoint. The raw storage path was being exposed
/// to the FE and 404'd on every page because no route served it.</item>
/// </list>
///
/// <para>Reuse this helper rather than re-deriving the strip/content-type logic at each exposure point.</para>
/// </summary>
public static class BrandingAssetUrls
{
    /// <summary>Public serving route for the main logo (see TenantSettingsController).</summary>
    public const string LogoRoutePath = "/api/v1/tenant/settings/branding/logo";

    /// <summary>Public serving route for the email-header logo (see TenantSettingsController).</summary>
    public const string EmailLogoRoutePath = "/api/v1/tenant/settings/branding/email-logo";

    /// <summary>Public serving route for the favicon (see TenantSettingsController).</summary>
    public const string FaviconRoutePath = "/api/v1/tenant/settings/branding/favicon";

    /// <summary>
    /// Public, subdomain-qualified serving route for a specific tenant's logo (DF-29). Used by the
    /// tenant-switcher, which lists OTHER tenants the user belongs to: the host-subdomain-resolved own-tenant
    /// <see cref="LogoRoutePath"/> endpoint can only serve the current tenant, so cross-tenant switcher logos are
    /// addressed by subdomain instead. Single-origin (path-qualified, not host-qualified) so it works in local
    /// dev, which has no wildcard DNS. The logo is public pre-auth branding, so serving it by subdomain is safe.
    /// </summary>
    public static string TenantLogoBySubdomainRoutePath(string subdomain)
        => $"/api/v1/tenant/{subdomain}/branding/logo";

    /// <summary>
    /// Converts a stored branding URL (<c>/{tenantId}/branding/{file}</c>) into the tenant-scoped relative path
    /// <see cref="Interfaces.IFileStorage.OpenReadAsync"/> expects (<c>branding/{file}</c>) — IFileStorage
    /// already prefixes the tenant id, so the leading <c>/{tenantId}/</c> segment is stripped when present.
    /// </summary>
    public static string ToStorageRelativePath(string storedUrl, Guid tenantId)
    {
        var path = storedUrl.TrimStart('/');
        var prefix = tenantId + "/";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            path = path[prefix.Length..];
        return path;
    }

    /// <summary>
    /// Derives the response content-type from a branding file's extension (the only types the upload validator
    /// admits are PNG/SVG/ICO); defaults to <c>image/png</c> for anything else.
    /// </summary>
    public static string ContentTypeFromExtension(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/png",
        };
    }

    /// <summary>
    /// Produces the FE-facing servable URL for a branding asset, or <c>null</c> when nothing is stored (so the
    /// FE shows its fallback instead of a broken image). <paramref name="baseUrl"/> is the current request
    /// origin (<c>scheme://host</c>); pass an empty string to emit a host-relative URL where no request context
    /// is available.
    /// </summary>
    public static string? Servable(string? storedPath, string baseUrl, string routePath)
        => string.IsNullOrWhiteSpace(storedPath) ? null : $"{baseUrl}{routePath}";
}
