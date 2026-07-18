using HRM.Domain.Enums;

namespace HRM.Application.Common.Helpers;

/// <summary>
/// Parses <see cref="EmploymentType"/> from a report-filter string, tolerating case, hyphens and spaces
/// (e.g. <c>"full-time"</c> / <c>"full time"</c> → <see cref="EmploymentType.FullTime"/>). Returns
/// <c>false</c> for null/blank/unrecognized input.
///
/// <para>Extracted from the identical private <c>TryParseEmploymentType</c> helper that was duplicated in
/// the Leave and Payroll report services. This strips <c>-</c>/space (the separators those report filters
/// use) — distinct from <see cref="EnumParsing.TryParseTolerant{TEnum}"/>, which strips <c>-</c>/<c>_</c> —
/// so the two are deliberately kept separate.</para>
/// </summary>
public static class EmploymentTypeParsing
{
    /// <summary>
    /// Parses <see cref="EmploymentType"/> from <paramref name="value"/>, tolerating case, hyphens and
    /// spaces. Returns <c>false</c> for null/blank/unrecognized input.
    /// </summary>
    public static bool TryParse(string? value, out EmploymentType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalised = value.Replace("-", string.Empty).Replace(" ", string.Empty);
        return Enum.TryParse(normalised, ignoreCase: true, out type);
    }
}
