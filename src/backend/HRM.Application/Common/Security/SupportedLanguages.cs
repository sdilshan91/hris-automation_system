namespace HRM.Application.Common.Security;

/// <summary>
/// US-ADM-006 AC-3/FR-4: the system-supported UI languages. A localization update's default language is
/// validated against this list; an unknown code is rejected. Kept as a small static list (English + Sinhala +
/// Tamil) matching the platform's i18n bundles — when more translation files ship, extend this list.
/// </summary>
public static class SupportedLanguages
{
    /// <summary>ISO-639-1 codes of the languages the platform ships UI bundles for.</summary>
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "en", // English
        "si", // Sinhala
        "ta", // Tamil
    };

    public static bool IsSupported(string? code)
        => !string.IsNullOrWhiteSpace(code) && Codes.Contains(code.Trim());
}
