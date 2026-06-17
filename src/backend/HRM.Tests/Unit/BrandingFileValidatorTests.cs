using System.Text;
using FluentAssertions;
using HRM.Application.Common.Security;

namespace HRM.Tests.Unit;

/// <summary>
/// US-ADM-006 NFR-2: pure unit tests for the magic-byte + size branding-file validator. This is the
/// acceptance for "validated server-side for file type (magic bytes, not just extension) and size".
/// </summary>
public sealed class BrandingFileValidatorTests
{
    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] IcoHeader = { 0x00, 0x00, 0x01, 0x00 };

    [Fact]
    public void Logo_RealPng_Accepted()
    {
        var result = BrandingFileValidator.Validate(BrandingAssetKind.Logo, PngHeader);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/png");
        result.Value.Extension.Should().Be(".png");
    }

    [Fact]
    public void Logo_RealSvg_Accepted()
    {
        var svg = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

        var result = BrandingFileValidator.Validate(BrandingAssetKind.Logo, svg);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/svg+xml");
    }

    [Fact]
    public void Logo_TextPretendingToBeSvg_Rejected()
    {
        var notSvg = Encoding.UTF8.GetBytes("just some plain text, not markup at all");

        var result = BrandingFileValidator.Validate(BrandingAssetKind.Logo, notSvg);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Unsupported file type");
    }

    [Fact]
    public void Logo_IcoRejected_BecauseLogoIsPngOrSvgOnly()
    {
        var result = BrandingFileValidator.Validate(BrandingAssetKind.Logo, IcoHeader);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Favicon_RealIco_Accepted()
    {
        var result = BrandingFileValidator.Validate(BrandingAssetKind.Favicon, IcoHeader);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/x-icon");
    }

    [Fact]
    public void Favicon_Png_Accepted()
    {
        var result = BrandingFileValidator.Validate(BrandingAssetKind.Favicon, PngHeader);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Logo_OverTwoMegabytes_Rejected()
    {
        var oversize = new byte[BrandingFileValidator.LogoMaxBytes + 1];
        Array.Copy(PngHeader, oversize, PngHeader.Length);

        var result = BrandingFileValidator.Validate(BrandingAssetKind.Logo, oversize);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("2 MB");
    }

    [Fact]
    public void Favicon_OverFiveHundredKilobytes_Rejected()
    {
        var oversize = new byte[BrandingFileValidator.FaviconMaxBytes + 1];
        Array.Copy(IcoHeader, oversize, IcoHeader.Length);

        var result = BrandingFileValidator.Validate(BrandingAssetKind.Favicon, oversize);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("500 KB");
    }

    [Fact]
    public void EmptyContent_Rejected()
    {
        BrandingFileValidator.Validate(BrandingAssetKind.Logo, Array.Empty<byte>())
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void PlanGate_NonEnterprise_ReturnsUpgradeMessage()
    {
        TenantPlanGate.EnsureEnterprise("starter").Should().Be(TenantPlanGate.UpgradeMessage);
        TenantPlanGate.EnsureEnterprise("enterprise-plus").Should().BeNull();
    }
}
