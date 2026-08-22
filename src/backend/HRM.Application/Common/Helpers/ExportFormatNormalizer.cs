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
    /// The canonical formats <see cref="Normalize"/> accepts, in display order.
    /// </summary>
    /// <remarks>
    /// ISSUE-379: the dashboard payload has to tell the FE which export buttons to render. Hardcoding that
    /// list at the call site would create a SECOND description of what this class already decides — the
    /// same S-1 shape that produced BUG-307 (ten copies of one rule) and BUG-311 (a union describing
    /// formats the wire never sent). The advertised list and the accepted list are one list, and
    /// <c>ExportFormatNormalizerTests</c> fails if they ever diverge.
    /// </remarks>
    public static readonly IReadOnlyList<string> Supported = ["csv", "xlsx", "pdf"];

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
