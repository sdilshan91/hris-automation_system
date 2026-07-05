namespace HRM.Application.Common.Security;

/// <summary>
/// US-ADM-006 AC-3/FR-4: the date-format tokens the platform supports. A localization update's date format is
/// validated against this list (BUG-005) — an unknown token is rejected. The set mirrors the front-end
/// <c>DATE_FORMATS</c> dropdown (company-settings model) so the FE↔BE contract stays aligned. Comparison is
/// case-SENSITIVE on purpose: the tokens are .NET custom date-format specifiers where case is significant
/// (e.g. <c>MM</c> = month vs <c>mm</c> = minute).
/// </summary>
public static class SupportedDateFormats
{
    /// <summary>The supported .NET custom date-format tokens (must match the FE DATE_FORMATS options).</summary>
    public static readonly IReadOnlySet<string> Formats = new HashSet<string>(StringComparer.Ordinal)
    {
        "dd MMM yyyy",
        "MM/dd/yyyy",
        "dd/MM/yyyy",
        "yyyy-MM-dd",
    };

    public static bool IsSupported(string? format)
        => !string.IsNullOrWhiteSpace(format) && Formats.Contains(format.Trim());
}
