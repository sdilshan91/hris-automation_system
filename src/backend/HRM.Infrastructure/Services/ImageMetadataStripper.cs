using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Strips EXIF/IPTC/XMP metadata (GPS coordinates, camera/device PII) from uploaded raster images before
/// storage (US-CHR-001 AC-4 / US-CHR-008 FR-5). Replaces the previous pass-through stubs in
/// <c>EmployeeService</c> and <c>EmployeeDocumentService</c>. Uses SixLabors.ImageSharp to decode, drop all
/// metadata profiles, and re-encode.
///
/// Testable + fail-open: <see cref="StripAsync"/> is a pure function over a stream. If the bytes are not a
/// decodable raster image (e.g. a PDF or Office document, or a corrupt/unsupported image), it returns the
/// ORIGINAL stream unchanged — it never throws and never blocks a legitimate non-image upload. Only real
/// JPEG/PNG images are re-encoded.
/// </summary>
public static class ImageMetadataStripper
{
    /// <summary>Content types we re-encode to strip metadata. Others pass through untouched.</summary>
    private static readonly HashSet<string> StrippableContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    /// <summary>
    /// Returns a metadata-stripped copy of <paramref name="input"/> for JPEG/PNG images; otherwise returns
    /// the original stream (repositioned to 0). <c>Replaced</c> is <c>true</c> when a NEW stream was
    /// allocated (the caller should dispose it after upload); <c>false</c> when the original is returned.
    /// </summary>
    public static async Task<(Stream Stream, bool Replaced)> StripAsync(
        Stream input,
        string? contentType,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (input.CanSeek)
            input.Position = 0;

        // Not an image type we strip (PDF/DOC/XLSX/…): pass through unchanged.
        if (string.IsNullOrWhiteSpace(contentType) || !StrippableContentTypes.Contains(contentType))
            return (input, false);

        try
        {
            using var image = await Image.LoadAsync(input, cancellationToken);

            // Drop all privacy-sensitive metadata profiles.
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            IImageEncoder encoder = contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                ? new PngEncoder()
                : new JpegEncoder();

            var output = new MemoryStream();
            await image.SaveAsync(output, encoder, cancellationToken);
            output.Position = 0;
            return (output, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not a decodable image (or an unsupported/corrupt one): degrade gracefully by passing the
            // original bytes through unchanged, per US-CHR-008 FR-5 ("only strip actual images").
            logger?.LogWarning(ex,
                "EXIF strip skipped: content declared as {ContentType} was not a decodable image; " +
                "passing the upload through unchanged.", contentType);

            if (input.CanSeek)
                input.Position = 0;
            return (input, false);
        }
    }
}
