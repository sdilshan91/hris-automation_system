using System.Globalization;

namespace HRM.Application.Common.Security;

/// <summary>
/// US-ADM-006 AC-3/FR-4: the set of valid ISO-4217 alphabetic currency codes. A localization update's currency
/// is validated against this set (BUG-005) — an unknown code is rejected. The list is derived once from the
/// runtime's culture data (the platform's own ISO-4217 knowledge) and seeded with the FE <c>CURRENCIES</c>
/// options so the FE↔BE contract currencies always validate regardless of the runtime's ICU data. Matching is
/// case-insensitive; callers should upper-case before storing.
/// </summary>
public static class IsoCurrencyCodes
{
    /// <summary>Valid ISO-4217 alphabetic currency codes known to the platform.</summary>
    public static readonly IReadOnlySet<string> Codes = BuildCodes();

    public static bool IsValid(string? code)
        => !string.IsNullOrWhiteSpace(code) && Codes.Contains(code.Trim());

    private static IReadOnlySet<string> BuildCodes()
    {
        // Guarantee the FE-offered currencies validate even if a trimmed-down ICU dataset omits some regions.
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "USD", "EUR", "GBP", "LKR", "INR", "AUD",
        };

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (!string.IsNullOrWhiteSpace(region.ISOCurrencySymbol))
                    set.Add(region.ISOCurrencySymbol);
            }
            catch (ArgumentException)
            {
                // Some specific cultures have no associated region — skip them.
            }
        }

        return set;
    }
}
