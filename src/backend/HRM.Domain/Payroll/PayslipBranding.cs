using HRM.Domain.Entities;

namespace HRM.Domain.Payroll;

/// <summary>
/// Per-tenant payslip branding (ISSUE-158, US-PAY-004/005) — the small, pure model the generation service
/// builds from the <see cref="Tenant"/> and passes into <see cref="PayslipDocumentModel"/>. Isolating the
/// branding resolution behind <see cref="From"/> keeps the mapping (name/address/colour fallbacks) unit
/// testable WITHOUT rendering a real PDF: the logo bytes are resolved separately (async, via IFileStorage)
/// and injected, so this stays a pure, IO-free transform.
/// </summary>
public sealed record PayslipBranding
{
    /// <summary>Company display name shown in the header. Never blank (falls back to a sane default).</summary>
    public required string CompanyName { get; init; }

    /// <summary>Registered/trading address, or null when the tenant has none.</summary>
    public string? CompanyAddress { get; init; }

    /// <summary>Primary brand colour (hex) applied to header/accent styling, or null for the default look.</summary>
    public string? PrimaryColor { get; init; }

    /// <summary>Logo image bytes (PNG/JPEG) already resolved from storage, or null to render without a logo.</summary>
    public byte[]? LogoBytes { get; init; }

    /// <summary>
    /// Builds the branding from a tenant. Pure — the caller resolves the logo bytes (via IFileStorage) and any
    /// fallback name up front. Applies defensive fallbacks: a blank name degrades to <paramref name="fallbackName"/>
    /// (typically the subdomain) then "Company"; blank address/colour degrade to null so the renderer uses its
    /// neutral default.
    /// </summary>
    public static PayslipBranding From(Tenant tenant, byte[]? logoBytes = null, string? fallbackName = null)
    {
        var name = FirstNonBlank(tenant.Name, fallbackName) ?? "Company";
        return new PayslipBranding
        {
            CompanyName = name,
            CompanyAddress = Blank(tenant.Address),
            PrimaryColor = Blank(tenant.PrimaryColor),
            LogoBytes = logoBytes,
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonBlank(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c)) return c.Trim();
        return null;
    }
}
