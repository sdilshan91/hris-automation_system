// ============================================================================
// Unit tests for ExportFormatNormalizer — the shared export-format normalizer extracted from the
// identical private NormalizeFormat helpers in the attendance/performance dashboard/summary services
// (reusability refactor). Behaviour must match the pre-extraction private copy exactly.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Helpers;
using Xunit;

namespace HRM.Tests.Unit;

public sealed class ExportFormatNormalizerTests
{
    [Theory]
    [InlineData("csv", "csv")]
    [InlineData("CSV", "csv")]
    [InlineData("  csv  ", "csv")]
    [InlineData("xlsx", "xlsx")]
    [InlineData("XLSX", "xlsx")]
    [InlineData("excel", "xlsx")]   // excel is an alias for xlsx
    [InlineData("Excel", "xlsx")]
    [InlineData("pdf", "pdf")]
    [InlineData("PDF", "pdf")]
    public void Normalize_returns_canonical_token_for_recognized_formats(string input, string expected)
        => ExportFormatNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("json")]
    [InlineData("txt")]
    public void Normalize_returns_null_for_null_blank_or_unrecognized(string? input)
        => ExportFormatNormalizer.Normalize(input).Should().BeNull();

    // ── ISSUE-379: the ADVERTISED list and the ACCEPTED list must be one list ──

    /// <summary>
    /// THE ARM THAT MATTERS. `PerformanceDashboardDto.AvailableExportFormats` is sourced from
    /// <see cref="ExportFormatNormalizer.Supported"/> so the dashboard advertises exactly what the export
    /// endpoint accepts. Every advertised token must therefore survive normalization to ITSELF — otherwise
    /// the UI renders a button the endpoint rejects with `invalid_format`.
    ///
    /// This invariant is not hypothetical. BUG-311 was this exact shape on the recommendation surface: a
    /// hand-written union claimed 'Excel' | 'Pdf' while the API sent csv/xlsx, hidden by an `as` cast,
    /// leaving a download-filename branch permanently dead. BUG-307 was the same shape, ten times over.
    /// </summary>
    [Fact]
    public void Every_advertised_format_is_accepted_and_already_canonical_ISSUE379()
    {
        ExportFormatNormalizer.Supported.Should().NotBeEmpty();

        foreach (var advertised in ExportFormatNormalizer.Supported)
        {
            ExportFormatNormalizer.Normalize(advertised).Should().Be(advertised,
                $"'{advertised}' is offered to the UI as an export button, so the endpoint must accept it "
                + "and treat it as already-canonical");
        }
    }

    /// <summary>
    /// The quieter half of the same drift: a format the normalizer accepts but the payload never advertises
    /// is a capability the UI silently hides.
    /// </summary>
    [Fact]
    public void The_advertised_set_is_exactly_the_reachable_canonical_set_ISSUE379()
    {
        var reachable = new[] { "csv", "xlsx", "excel", "pdf" }
            .Select(ExportFormatNormalizer.Normalize)
            .Where(f => f is not null)
            .Distinct()
            .ToList();

        reachable.Should().BeEquivalentTo(ExportFormatNormalizer.Supported,
            "one advertised but not accepted is a button that 400s; one accepted but not advertised is a "
            + "capability the product has and never offers");
    }

    /// <summary>
    /// A DISPLAY label must never be mistaken for a wire token. The FE shows "Excel (XLSX)"; if that string
    /// ever reached the API it would 400, and BUG-311 showed how easily display copy and wire tokens get
    /// conflated on this exact surface.
    /// </summary>
    [Fact]
    public void Display_labels_are_not_wire_tokens_ISSUE379()
        => ExportFormatNormalizer.Normalize("Excel (XLSX)").Should().BeNull();
}
