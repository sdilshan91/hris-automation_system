namespace HRM.Application.Common.Helpers;

/// <summary>
/// Shared enum-parsing helpers. The API accepts enum values in several textual forms — the PascalCase enum
/// name (<c>BalanceSummary</c>) and the kebab/snake-case forms the FE + IEEE-829 test cases use
/// (<c>balance-summary</c>, <c>lop_summary</c>). <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
/// is case-insensitive but NOT separator-insensitive, so this strips <c>-</c>/<c>_</c> first.
///
/// <para>Extracted from a per-controller private helper (ISSUE-047) so the report/export/format endpoints
/// across modules (HR / Leave / Payroll reports, audit, notification prefs, offboarding, …) share ONE
/// tolerant parser instead of re-declaring it. Prefer this over a private <c>TryParse*</c> method.</para>
/// </summary>
public static class EnumParsing
{
    /// <summary>
    /// Parses <typeparamref name="TEnum"/> from <paramref name="value"/>, tolerating case and
    /// hyphen/underscore separators (e.g. <c>"balance-summary"</c> → <c>BalanceSummary</c>). Returns
    /// <c>false</c> for null/blank/unrecognized input.
    /// </summary>
    public static bool TryParseTolerant<TEnum>(string? value, out TEnum result)
        where TEnum : struct
    {
        var normalized = value?.Replace("-", string.Empty).Replace("_", string.Empty);
        return Enum.TryParse(normalized, ignoreCase: true, out result);
    }
}
