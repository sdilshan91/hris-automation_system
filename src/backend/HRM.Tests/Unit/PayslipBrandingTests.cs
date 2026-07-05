// ============================================================================
// ISSUE-158: PayslipBranding.From — pure tenant→branding mapping (no PDF render).
// TC-PAY-158-01..07. Asserts the name/address/colour fallbacks and logo passthrough
// exactly. This is the fully-assertable seam for the branding feature.
//
// ParseColor NOTE: PayslipPdfRenderer.ParseColor is PRIVATE and its only public seam
// is PayslipPdfRenderer.Render, which produces a real PDF — and this brief says do NOT
// render one here. The colour value is not observable in the PDF byte stream, so a
// hex→colour assertion would be theater. It is therefore intentionally NOT tested at
// this layer (non-throw of the render seam with a brand colour is already covered by
// PayslipPdfRendererTests.Render_WithTenantBrandColor_DoesNotThrow). Documented default
// for the record: a blank/invalid hex falls back to QuestPDF Colors.Blue.Darken2.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Payroll;

namespace HRM.Tests.Unit;

public sealed class PayslipBrandingTests
{
    private static Tenant TenantWith(string? name = null, string? address = null, string? primaryColor = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Subdomain = "acme",
            Name = name ?? string.Empty,
            Address = address,
            PrimaryColor = primaryColor,
        };

    // ── CompanyName: tenant name wins when present (and is trimmed) ──────────
    [Fact]
    public void PayslipBranding_NamePresent_UsesTenantName_ISSUE158()
    {
        var branding = PayslipBranding.From(TenantWith(name: "  Acme Corp  "), fallbackName: "acme");

        branding.CompanyName.Should().Be("Acme Corp");   // trimmed, tenant name preferred over fallback
    }

    // ── CompanyName: blank tenant name degrades to the fallback ─────────────
    [Fact]
    public void PayslipBranding_BlankName_FallsBack_ISSUE158()
    {
        var branding = PayslipBranding.From(TenantWith(name: "   "), fallbackName: "acme");

        branding.CompanyName.Should().Be("acme");
    }

    // ── CompanyName: both blank → the hard default "Company" ────────────────
    [Fact]
    public void PayslipBranding_BothBlank_DefaultsToCompany_ISSUE158()
    {
        var branding = PayslipBranding.From(TenantWith(name: ""), fallbackName: "   ");

        branding.CompanyName.Should().Be("Company");
    }

    // ── CompanyAddress: whitespace → null; present → trimmed ────────────────
    [Fact]
    public void PayslipBranding_BlankAddress_Null_ISSUE158()
    {
        PayslipBranding.From(TenantWith(name: "Acme", address: "   ")).CompanyAddress.Should().BeNull();
        PayslipBranding.From(TenantWith(name: "Acme", address: " 1 Industrial Way ")).CompanyAddress.Should().Be("1 Industrial Way");
    }

    // ── PrimaryColor: whitespace → null; present → passed through (trimmed) ──
    [Fact]
    public void PayslipBranding_BlankColor_Null_ISSUE158()
    {
        PayslipBranding.From(TenantWith(name: "Acme", primaryColor: "  ")).PrimaryColor.Should().BeNull();
        PayslipBranding.From(TenantWith(name: "Acme", primaryColor: " #1d4ed8 ")).PrimaryColor.Should().Be("#1d4ed8");
    }

    // ── LogoBytes: passed through verbatim onto the branding ────────────────
    [Fact]
    public void PayslipBranding_LogoBytes_PassedThrough_ISSUE158()
    {
        var logo = new byte[] { 0x89, 0x50, 0x4E, 0x47 };   // PNG magic bytes

        var branding = PayslipBranding.From(TenantWith(name: "Acme"), logoBytes: logo);

        branding.LogoBytes.Should().BeSameAs(logo);
    }

    // ── LogoBytes: omitted (default) → null (render without a logo) ─────────
    [Fact]
    public void PayslipBranding_NoLogo_Null_ISSUE158()
    {
        PayslipBranding.From(TenantWith(name: "Acme")).LogoBytes.Should().BeNull();
    }
}
