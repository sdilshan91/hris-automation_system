using HRM.Application.Common.Models;

namespace HRM.Application.Common.Security;

/// <summary>
/// The kind of branding asset being uploaded (US-ADM-006 AC-2 / FR-2). Each kind has its own allowed file
/// types and size cap.
/// </summary>
public enum BrandingAssetKind
{
    /// <summary>Main logo (sidebar / PDF). PNG or SVG, max 2 MB.</summary>
    Logo,

    /// <summary>Email-header logo. PNG or SVG, max 2 MB (same constraints as the main logo).</summary>
    EmailLogo,

    /// <summary>Browser favicon. ICO or PNG, max 500 KB.</summary>
    Favicon,
}

/// <summary>The detected, validated content type + canonical file extension for a branding upload.</summary>
public sealed record BrandingFileType(string ContentType, string Extension);

/// <summary>
/// Pure, server-side validation of branding-file uploads by MAGIC BYTES (not extension) AND size
/// (US-ADM-006 NFR-2). This is the unit-testable acceptance for NFR-2: a file whose bytes are not a real
/// PNG/SVG/ICO is rejected even if its name ends in ".png"; an oversize file is rejected. The validator does
/// NOT touch storage — it only inspects the byte content the caller already buffered.
/// </summary>
public static class BrandingFileValidator
{
    public const long LogoMaxBytes = 2 * 1024 * 1024;     // 2 MB (FR-2)
    public const long FaviconMaxBytes = 500 * 1024;       // 500 KB (FR-2)

    // ── Magic-byte signatures ────────────────────────────────────────────────
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] IcoMagic = { 0x00, 0x00, 0x01, 0x00 }; // ICONDIR header

    /// <summary>
    /// Validates the buffered <paramref name="content"/> for the given <paramref name="kind"/>. On success
    /// returns the detected (real) content type + canonical extension; on failure a 400 Result with a clear
    /// message. Detection is by content, never by the supplied file name.
    /// </summary>
    public static Result<BrandingFileType> Validate(BrandingAssetKind kind, byte[] content)
    {
        if (content is null || content.Length == 0)
            return Result<BrandingFileType>.Failure("The uploaded file is empty.", 400);

        var maxBytes = kind == BrandingAssetKind.Favicon ? FaviconMaxBytes : LogoMaxBytes;
        if (content.Length > maxBytes)
        {
            var label = kind == BrandingAssetKind.Favicon ? "Favicon" : "Logo";
            var limit = kind == BrandingAssetKind.Favicon ? "500 KB" : "2 MB";
            return Result<BrandingFileType>.Failure($"{label} exceeds the maximum size of {limit}.", 400);
        }

        var detected = Detect(content);
        if (detected is null)
            return InvalidType(kind);

        var allowed = kind switch
        {
            BrandingAssetKind.Favicon => detected.ContentType is "image/x-icon" or "image/png",
            _ => detected.ContentType is "image/png" or "image/svg+xml",
        };

        return allowed ? Result<BrandingFileType>.Success(detected) : InvalidType(kind);
    }

    private static Result<BrandingFileType> InvalidType(BrandingAssetKind kind)
    {
        var allowed = kind == BrandingAssetKind.Favicon ? "ICO or PNG" : "PNG or SVG";
        return Result<BrandingFileType>.Failure(
            $"Unsupported file type. Allowed types: {allowed}.", 400);
    }

    /// <summary>
    /// Sniffs the real content type from the leading bytes. Returns null when the bytes match no supported
    /// signature. PNG and ICO are detected by magic bytes; SVG is XML/text so it is detected structurally.
    /// </summary>
    private static BrandingFileType? Detect(byte[] content)
    {
        if (StartsWith(content, PngMagic))
            return new BrandingFileType("image/png", ".png");

        if (StartsWith(content, IcoMagic))
            return new BrandingFileType("image/x-icon", ".ico");

        if (LooksLikeSvg(content))
            return new BrandingFileType("image/svg+xml", ".svg");

        return null;
    }

    private static bool StartsWith(byte[] content, byte[] signature)
    {
        if (content.Length < signature.Length)
            return false;

        for (var i = 0; i < signature.Length; i++)
        {
            if (content[i] != signature[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// SVG has no binary magic number — it is XML. Treat the leading window (skipping a UTF-8 BOM and
    /// leading whitespace) as text and require an XML/SVG opening token, with an "&lt;svg" appearing in the
    /// sniff window. This rejects arbitrary text files that merely end in ".svg".
    /// </summary>
    private static bool LooksLikeSvg(byte[] content)
    {
        var start = 0;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            start = 3; // skip UTF-8 BOM

        var window = content.Length - start;
        if (window <= 0)
            return false;

        var text = System.Text.Encoding.UTF8.GetString(content, start, Math.Min(window, 1024));
        var trimmed = text.TrimStart();

        if (!trimmed.StartsWith('<'))
            return false;

        return trimmed.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }
}
