using System.Text.RegularExpressions;

namespace HRM.Application.Common.Helpers;

/// <summary>
/// Shared, allocation-light validator for DNS / email domain names (e.g. "contoso.com"). Used by the SSO
/// allow-list validation (US-AUTH-012 FR-4) and available for reuse by any feature that must accept a bare
/// domain. Rules: one or more dot-separated labels; each label 1-63 chars of [a-z0-9-], not starting/ending with
/// a hyphen; a final TLD label of at least two letters; total length ≤ 253. Case-insensitive.
/// </summary>
public static partial class DomainNameValidator
{
    [GeneratedRegex(
        @"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DomainRegex();

    /// <summary>Returns true when <paramref name="value"/> is a syntactically valid domain name.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DomainRegex().IsMatch(value.Trim());
}
