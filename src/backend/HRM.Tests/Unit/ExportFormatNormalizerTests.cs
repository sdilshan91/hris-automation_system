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
}
