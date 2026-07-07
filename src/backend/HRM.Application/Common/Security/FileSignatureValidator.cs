using HRM.Application.Common.Models;

namespace HRM.Application.Common.Security;

/// <summary>
/// Pure, server-side validation that an upload's REAL byte content matches its declared content type
/// (BUG-058). Upload paths previously trusted only the client-supplied <c>Content-Type</c>, so a renamed
/// <c>.exe</c> (or any spoofed type) whose MIME string was in the allow-list would be accepted. This
/// validator sniffs the leading "magic bytes" and rejects anything whose signature does not match the
/// claimed type. It maps each accepted content type to its expected leading-byte signature(s) — the same
/// magic-byte approach as <see cref="BrandingFileValidator"/>, generalized to the document/image types the
/// resume, employee-document, and self-assessment-attachment paths accept.
///
/// Fail-closed: a content type this validator does not know how to sniff is rejected. Every type in the
/// upload paths' allow-lists is mapped below, so a legitimate, correctly-typed upload always passes; an
/// unknown/spoofed type does not.
/// </summary>
public static class FileSignatureValidator
{
    /// <summary>Machine-readable error code returned when the bytes do not match the declared type.</summary>
    public const string ErrorCode = "invalid_file_type";

    private const string RejectMessage =
        "The uploaded file's content does not match its declared type.";

    /// <summary>Number of leading bytes to read for sniffing (covers the WebP "WEBP" tag at offset 8).</summary>
    private const int SniffLength = 16;

    /// <summary>A magic-byte segment that must appear at a fixed <paramref name="Offset"/>.</summary>
    private sealed record Segment(int Offset, byte[] Bytes);

    private static Segment Seg(int offset, params byte[] bytes) => new(offset, bytes);

    // OOXML (.docx/.xlsx) are ZIP containers; a container holding entries always starts with the local
    // file header "PK\x03\x04". The empty/spanned ZIP variants are included defensively.
    private static readonly Segment[][] Zip =
    {
        new[] { Seg(0, 0x50, 0x4B, 0x03, 0x04) },
        new[] { Seg(0, 0x50, 0x4B, 0x05, 0x06) },
        new[] { Seg(0, 0x50, 0x4B, 0x07, 0x08) },
    };

    // Legacy Office (.doc/.xls) are OLE2 compound files with the D0CF11E0A1B11AE1 header.
    private static readonly Segment[][] Ole =
    {
        new[] { Seg(0, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1) },
    };

    /// <summary>
    /// content type -> alternative signatures (OR). Each signature is a set of segments that must ALL match
    /// (AND). Detection is by bytes only — never by file name or the caller's declared string beyond using
    /// it to select the expected signature set.
    /// </summary>
    private static readonly Dictionary<string, Segment[][]> Signatures =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = new[] { new[] { Seg(0, 0x25, 0x50, 0x44, 0x46) } }, // %PDF
            ["image/png"] = new[] { new[] { Seg(0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) } },
            ["image/jpeg"] = new[] { new[] { Seg(0, 0xFF, 0xD8, 0xFF) } },
            // WebP: "RIFF" then (4-byte size) then "WEBP" at offset 8.
            ["image/webp"] = new[] { new[] { Seg(0, 0x52, 0x49, 0x46, 0x46), Seg(8, 0x57, 0x45, 0x42, 0x50) } },
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = Zip, // .docx
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = Zip,       // .xlsx
            ["application/msword"] = Ole,        // .doc
            ["application/vnd.ms-excel"] = Ole,  // .xls
        };

    /// <summary>
    /// Pure, unit-testable core: validates that <paramref name="header"/> (the leading bytes of the file)
    /// matches the signature expected for <paramref name="contentType"/>. Returns a 400
    /// <c>invalid_file_type</c> failure when the bytes don't match or the type is unknown.
    /// </summary>
    public static Result Validate(string? contentType, ReadOnlySpan<byte> header)
    {
        if (string.IsNullOrWhiteSpace(contentType) || !Signatures.TryGetValue(contentType, out var alternatives))
            return Result.Failure(RejectMessage, 400, ErrorCode);

        foreach (var signature in alternatives)
        {
            var allMatch = true;
            foreach (var segment in signature)
            {
                if (!MatchesAt(header, segment))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
                return Result.Success();
        }

        return Result.Failure(RejectMessage, 400, ErrorCode);
    }

    /// <summary>
    /// Reads the first <see cref="SniffLength"/> bytes of <paramref name="stream"/>, validates them against
    /// <paramref name="contentType"/>, and resets the stream to position 0 (for the subsequent virus scan /
    /// upload). The stream must be seekable — the upload paths already rely on seeking to re-read after the
    /// scan.
    /// </summary>
    public static async Task<Result> ValidateStreamAsync(
        string? contentType, Stream stream, CancellationToken cancellationToken = default)
    {
        var header = await ReadHeaderAsync(stream, cancellationToken);
        return Validate(contentType, header);
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        var buffer = new byte[SniffLength];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
                break;
            total += read;
        }

        if (stream.CanSeek)
            stream.Position = 0;

        // Return only the bytes actually read so a short file doesn't false-match trailing zeros.
        return total == buffer.Length ? buffer : buffer[..total];
    }

    private static bool MatchesAt(ReadOnlySpan<byte> header, Segment segment)
    {
        if (header.Length < segment.Offset + segment.Bytes.Length)
            return false;

        for (var i = 0; i < segment.Bytes.Length; i++)
        {
            if (header[segment.Offset + i] != segment.Bytes[i])
                return false;
        }

        return true;
    }
}
