using FluentAssertions;
using HRM.Application.Common.Security;

namespace HRM.Tests.Unit;

/// <summary>
/// BUG-058: pure unit tests for the magic-byte upload-content sniffer
/// (<see cref="FileSignatureValidator"/>). Upload paths previously trusted the client-supplied
/// <c>Content-Type</c>, so a renamed <c>.exe</c> whose MIME string was in the allow-list was accepted.
/// These tests exercise the pure <c>Validate(contentType, header)</c> core with exact byte arrays (no
/// files) plus the seekable-stream wrapper's position reset.
///
/// Pre-fix reasoning: <see cref="FileSignatureValidator"/> did not exist, so none of these rejections
/// happened — a PNG/JPEG/MZ payload declared as <c>application/pdf</c> flowed straight through.
/// </summary>
public sealed class FileSignatureValidatorTests
{
    // ── Real, correct signatures per mapped content-type ──────────────────────────────────────────
    private static readonly byte[] Pdf = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };          // %PDF-1.7
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };                       // PK\x03\x04 (docx/xlsx)
    private static readonly byte[] Ole = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };            // OLE2 (doc/xls)
    // WebP: "RIFF" + 4-byte size + "WEBP" at offset 8.
    private static readonly byte[] Webp =
        { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20 };

    private static readonly byte[] MzExe = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 };                      // "MZ" DOS/PE header

    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEnumerable<object[]> CorrectSignatures() => new[]
    {
        new object[] { "application/pdf", Pdf },
        new object[] { "image/png", Png },
        new object[] { "image/jpeg", Jpeg },
        new object[] { "image/webp", Webp },
        new object[] { Docx, Zip },
        new object[] { Xlsx, Zip },
        new object[] { "application/msword", Ole },
        new object[] { "application/vnd.ms-excel", Ole },
    };

    [Theory]
    [MemberData(nameof(CorrectSignatures))]
    public void FileSignature_CorrectBytes_Ok_BUG058(string contentType, byte[] header)
    {
        var result = FileSignatureValidator.Validate(contentType, header);

        result.IsSuccess.Should().BeTrue(
            "{0} bytes match the signature mapped for {1}", BitConverter.ToString(header), contentType);
    }

    [Fact]
    public void FileSignature_PdfBytes_Ok_BUG058()
    {
        FileSignatureValidator.Validate("application/pdf", Pdf).IsSuccess.Should().BeTrue();
    }

    // ── Spoof: allow-listed content-type but the bytes are something else ──────────────────────────

    [Fact]
    public void FileSignature_PngBytesClaimedPdf_Rejected_BUG058()
    {
        var result = FileSignatureValidator.Validate("application/pdf", Png);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(FileSignatureValidator.ErrorCode); // "invalid_file_type"
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public void FileSignature_JpegBytesClaimedPng_Rejected_BUG058()
    {
        var result = FileSignatureValidator.Validate("image/png", Jpeg);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_file_type");
    }

    [Fact]
    public void FileSignature_MzExe_Rejected_BUG058()
    {
        // A renamed executable declared as a PDF — the exact BUG-058 attack.
        var result = FileSignatureValidator.Validate("application/pdf", MzExe);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_file_type");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public void FileSignature_WebpMissingWebpTag_Rejected_BUG058()
    {
        // Valid "RIFF" lead-in but no "WEBP" at offset 8 (e.g. a WAV/AVI RIFF container) claimed as webp.
        var riffNotWebp = new byte[]
            { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x41, 0x56, 0x49, 0x20, 0x00, 0x00, 0x00, 0x00 };

        FileSignatureValidator.Validate("image/webp", riffNotWebp).IsFailure.Should().BeTrue();
    }

    // ── Fail-closed: unknown / blank content-type ─────────────────────────────────────────────────

    [Fact]
    public void FileSignature_UnmappedContentType_Rejected_BUG058()
    {
        var result = FileSignatureValidator.Validate("application/x-msdownload", MzExe);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_file_type");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FileSignature_BlankContentType_Rejected_BUG058(string? contentType)
    {
        FileSignatureValidator.Validate(contentType, Pdf).IsFailure.Should().BeTrue();
    }

    // ── Boundary: header shorter than the expected signature must not crash ────────────────────────

    [Fact]
    public void FileSignature_TooShortHeader_Rejected_NoCrash_BUG058()
    {
        // Only 2 bytes given where PNG needs 8, and webp needs bytes at offset 8.
        var twoBytes = new byte[] { 0x89, 0x50 };

        FileSignatureValidator.Validate("image/png", twoBytes).IsFailure.Should().BeTrue();

        // Sniffing must never index past the supplied header (webp checks bytes at offset 8).
        var probe = () => FileSignatureValidator.Validate("image/webp", twoBytes);
        probe.Should().NotThrow();
        probe().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void FileSignature_EmptyHeader_Rejected_BUG058()
    {
        FileSignatureValidator.Validate("application/pdf", ReadOnlySpan<byte>.Empty).IsFailure.Should().BeTrue();
    }

    // ── Stream wrapper resets Position for the downstream virus scan / upload ──────────────────────

    [Fact]
    public async Task FileSignature_StreamResetAfterValidate_BUG058()
    {
        using var stream = new MemoryStream();
        stream.Write(Pdf, 0, Pdf.Length);
        stream.Position = stream.Length; // caller left it at the end

        var result = await FileSignatureValidator.ValidateStreamAsync("application/pdf", stream);

        result.IsSuccess.Should().BeTrue();
        stream.Position.Should().Be(0, "the stream must be rewound for the subsequent scan/upload");
    }

    [Fact]
    public async Task FileSignature_StreamSpoof_Rejected_AndReset_BUG058()
    {
        using var stream = new MemoryStream();
        stream.Write(MzExe, 0, MzExe.Length);
        stream.Position = 0;

        var result = await FileSignatureValidator.ValidateStreamAsync("application/pdf", stream);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_file_type");
        stream.Position.Should().Be(0);
    }
}
