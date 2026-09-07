using System.Text.RegularExpressions;

namespace HRM.Application.Common.Helpers;

/// <summary>
/// Validates a tenant's stored primary colour before it is handed to a PDF renderer.
///
/// <para>QuestPDF throws on a malformed colour string, which would fail a whole export, so every branded
/// renderer has to guard the tenant value. That guard existed as a private <c>ResolveBrandColor</c> copy in
/// <c>PerformanceDashboardService</c> and <c>PerformancePdfRenderer</c>; this is the shared one. Prefer it
/// over re-declaring a private copy.</para>
/// </summary>
public static partial class BrandColor
{
    /// <summary>Header colour used when the tenant has no valid primary colour set.</summary>
    public const string Default = "#1E3A8A";

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();

    /// <summary>
    /// Returns <paramref name="brandColor"/> when it is a valid <c>#RRGGBB</c>/<c>#RGB</c> hex, otherwise
    /// <see cref="Default"/>.
    /// </summary>
    public static string Resolve(string? brandColor)
    {
        var c = brandColor?.Trim();
        return !string.IsNullOrEmpty(c) && HexColor().IsMatch(c) ? c : Default;
    }
}
