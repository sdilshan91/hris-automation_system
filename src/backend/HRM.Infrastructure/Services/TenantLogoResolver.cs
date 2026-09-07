using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Loads a tenant's stored logo as bytes a QuestPDF renderer can embed, degrading to <c>null</c> (never
/// throwing) on anything that would otherwise fail a whole export.
///
/// <para>The ISSUE-158 constraint: the stored <c>Tenant.LogoUrl</c> is the app-INTERNAL storage path
/// (<c>/{tenantId}/branding/logo.png</c>), so the bytes are read in-process through
/// <see cref="IFileStorage"/> — a renderer NEVER makes an outbound HTTP fetch. It returns <c>null</c> for
/// each of: blank URL, an absolute http(s) URL, a missing file, zero-length content, or bytes QuestPDF
/// cannot decode; the PDF then renders with the colour band and title only.</para>
///
/// <para>This is the shared version of the private <c>ResolveLogoBytesAsync</c>/<c>IsRenderableImage</c>
/// pair that <c>PayslipBatchRenderer</c> and <c>PerformanceDashboardService</c> each carry a copy of.
/// Prefer this over re-declaring a third.</para>
/// </summary>
public static class TenantLogoResolver
{
    /// <summary>
    /// Reads the logo bytes for <paramref name="tenantId"/> from <paramref name="storedLogoUrl"/>, or
    /// <c>null</c> when unavailable/undecodable. <paramref name="context"/> only labels the warning log
    /// (e.g. "Recruitment dashboard").
    /// </summary>
    public static async Task<byte[]?> ResolveAsync(
        IFileStorage fileStorage,
        Guid tenantId,
        string? storedLogoUrl,
        ILogger logger,
        string context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storedLogoUrl))
            return null;

        var trimmed = storedLogoUrl.Trim();
        // An external asset is never fetched by a renderer (ISSUE-158 constraint) — degrade to no-logo.
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        var relativePath = BrandingAssetUrls.ToStorageRelativePath(trimmed, tenantId);
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        try
        {
            await using var stream = await fileStorage.OpenReadAsync(tenantId, relativePath, ct);
            if (stream is null)
                return null;

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (bytes.Length == 0)
                return null;

            // Validate the bytes decode as an image QuestPDF can render, so a corrupt asset degrades to
            // "no logo" rather than throwing mid-render (which would fail the whole export).
            if (!IsRenderableImage(bytes))
            {
                logger.LogWarning(
                    "{Context} logo bytes were not a decodable image; rendering without a logo. Tenant={TenantId}, Path={Path}",
                    context, tenantId, relativePath);
                return null;
            }

            return bytes;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "{Context} logo could not be loaded; rendering without a logo. Tenant={TenantId}, Path={Path}",
                context, tenantId, relativePath);
            return null;
        }
    }

    /// <summary>True when the bytes decode as an image QuestPDF can render (graceful-degrade check).</summary>
    private static bool IsRenderableImage(byte[] bytes)
    {
        try
        {
            _ = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
