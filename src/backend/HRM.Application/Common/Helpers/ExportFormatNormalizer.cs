namespace HRM.Application.Common.Helpers;

/// <summary>
/// Normalizes a user-supplied export-format string to its canonical lowercase token — <c>csv</c>,
/// <c>xlsx</c> (with <c>excel</c> accepted as an alias) or <c>pdf</c> — returning <c>null</c> for
/// null/blank/unrecognized input.
///
/// <para>Extracted from the identical private <c>NormalizeFormat</c> helper that was duplicated across the
/// dashboard/summary export services (attendance dashboard + summary, performance dashboard). Prefer this
/// over re-declaring a private copy. Note: some export surfaces intentionally support a narrower set of
/// formats (e.g. the recruitment dashboard has no PDF export) and keep their own normalizer.</para>
/// </summary>
public static class ExportFormatNormalizer
{
    /// <summary>
    /// Returns the canonical export-format token (<c>csv</c>/<c>xlsx</c>/<c>pdf</c>) for
    /// <paramref name="format"/>, or <c>null</c> when the input is null/blank/unrecognized.
    /// </summary>
    public static string? Normalize(string? format)
    {
        var f = format?.Trim().ToLowerInvariant();
        return f switch
        {
            "csv" => "csv",
            "xlsx" or "excel" => "xlsx",
            "pdf" => "pdf",
            _ => null,
        };
    }
}
