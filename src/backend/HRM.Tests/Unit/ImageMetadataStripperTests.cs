using System.Text;
using FluentAssertions;
using HRM.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace HRM.Tests.Unit;

/// <summary>
/// ISSUE-CHR001 / US-CHR-001 AC-4: EXIF/IPTC/XMP metadata (GPS, camera/device PII) must be stripped from
/// uploaded JPEG/PNG images before storage (<see cref="ImageMetadataStripper"/>). These tests build a real
/// in-memory image, embed EXIF, prove the fixture round-trips the metadata, then assert the stripper
/// removes it and re-encodes (Replaced == true). Non-image and corrupt inputs must pass through unchanged
/// without throwing.
///
/// Pre-fix reasoning: the upload paths used pass-through stubs, so EXIF was persisted verbatim — the
/// "re-loaded output has no ExifProfile" assertion below would have failed.
/// </summary>
public sealed class ImageMetadataStripperTests
{
    private const string Copyright = "TechOne Global 2026 — GPS 6.9271,79.8612";

    private static async Task<MemoryStream> BuildImageWithExifAsync(string contentType)
    {
        using var image = new Image<Rgba32>(8, 8);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Copyright, Copyright);
        image.Metadata.ExifProfile = exif;

        var ms = new MemoryStream();
        if (contentType == "image/png")
            await image.SaveAsync(ms, new PngEncoder());
        else
            await image.SaveAsync(ms, new JpegEncoder());
        ms.Position = 0;
        return ms;
    }

    private static async Task<ExifProfile?> ReadExifAsync(Stream stream)
    {
        stream.Position = 0;
        using var image = await Image.LoadAsync(stream);
        stream.Position = 0;
        return image.Metadata.ExifProfile;
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task ExifStrip_ImageWithExif_RemovesMetadata_ISSUE_CHR001(string contentType)
    {
        await using var source = await BuildImageWithExifAsync(contentType);

        // Fixture pre-condition: the source really carries the EXIF we embedded (otherwise the test is empty).
        var sourceExif = await ReadExifAsync(source);
        sourceExif.Should().NotBeNull("the fixture must actually embed EXIF for the strip to be meaningful");
        var copy = sourceExif!.GetValue(ExifTag.Copyright);
        copy.Should().NotBeNull();
        copy!.Value.Should().Be(Copyright);
        source.Position = 0;

        var (output, replaced) = await ImageMetadataStripper.StripAsync(source, contentType);

        replaced.Should().BeTrue("a real JPEG/PNG is re-encoded to drop metadata");
        output.Should().NotBeSameAs(source, "a new stream is allocated for the stripped copy");

        var strippedExif = await ReadExifAsync(output);
        strippedExif.Should().BeNull("all EXIF/IPTC/XMP profiles must be dropped before storage");

        await output.DisposeAsync();
    }

    // Load-bearing JPEG-named test (per brief), reusing the theory body's assertions explicitly.
    [Fact]
    public async Task ExifStrip_JpegWithExif_RemovesMetadata_ISSUE_CHR001()
    {
        await using var source = await BuildImageWithExifAsync("image/jpeg");
        (await ReadExifAsync(source)).Should().NotBeNull();
        source.Position = 0;

        var (output, replaced) = await ImageMetadataStripper.StripAsync(source, "image/jpeg");

        replaced.Should().BeTrue();
        (await ReadExifAsync(output)).Should().BeNull();
        await output.DisposeAsync();
    }

    [Fact]
    public async Task ExifStrip_NonImage_PassThrough()
    {
        // A PDF declared as application/pdf must be returned untouched (same reference, not re-encoded).
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n...binary body...");
        await using var source = new MemoryStream(pdfBytes, writable: false);

        var (output, replaced) = await ImageMetadataStripper.StripAsync(source, "application/pdf");

        replaced.Should().BeFalse();
        output.Should().BeSameAs(source, "non-image uploads are passed through unchanged");
        output.Position.Should().Be(0);
    }

    [Fact]
    public async Task ExifStrip_BlankContentType_PassThrough()
    {
        await using var source = new MemoryStream(Encoding.ASCII.GetBytes("plain text"));

        var (output, replaced) = await ImageMetadataStripper.StripAsync(source, contentType: "");

        replaced.Should().BeFalse();
        output.Should().BeSameAs(source);
    }

    [Fact]
    public async Task ExifStrip_CorruptImage_NoThrow_PassThrough()
    {
        // Random bytes claiming to be a PNG: ImageSharp cannot decode → degrade gracefully, no throw.
        var junk = new byte[256];
        new Random(42).NextBytes(junk);
        await using var source = new MemoryStream(junk);

        var act = () => ImageMetadataStripper.StripAsync(source, "image/png");

        var (output, replaced) = (await act.Should().NotThrowAsync()).Subject;
        replaced.Should().BeFalse("an undecodable image is passed through, not re-encoded");
        output.Should().BeSameAs(source);
        output.Position.Should().Be(0, "the stream is reset for the downstream upload");
    }
}
