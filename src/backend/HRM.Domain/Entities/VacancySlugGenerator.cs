using System.Globalization;
using System.Text;

namespace HRM.Domain.Entities;

/// <summary>
/// Pure helper that produces SEO-friendly URL slugs for published vacancies (US-REC-001 FR-5).
/// Kept in the domain so the service and tests share identical slug rules. Uniqueness-per-tenant is
/// the service's responsibility (it appends a numeric suffix on collision).
/// </summary>
public static class VacancySlugGenerator
{
    private const int MaxBaseLength = 80;

    /// <summary>
    /// Builds the base slug from a vacancy title: lower-case, ASCII-folded, non-alphanumerics
    /// collapsed to single hyphens, trimmed. Returns "vacancy" if nothing usable remains.
    /// </summary>
    public static string Generate(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "vacancy";

        // Fold accents (é -> e) then drop combining marks.
        var normalized = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasHyphen = false;

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && sb.Length > 0)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0)
            return "vacancy";

        if (slug.Length > MaxBaseLength)
            slug = slug[..MaxBaseLength].Trim('-');

        return slug.Length == 0 ? "vacancy" : slug;
    }
}
