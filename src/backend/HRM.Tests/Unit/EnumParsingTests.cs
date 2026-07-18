// ============================================================================
// Direct unit tests for the shared EnumParsing.TryParseTolerant helper (previously only covered indirectly
// via controller tests). Tolerates case + hyphen/underscore separators; false for null/blank/unknown.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Helpers;
using Xunit;

namespace HRM.Tests.Unit;

public sealed class EnumParsingTests
{
    public enum Sample
    {
        BalanceSummary,
        LopSummary,
    }

    [Theory]
    [InlineData("BalanceSummary", Sample.BalanceSummary)]
    [InlineData("balancesummary", Sample.BalanceSummary)]
    [InlineData("balance-summary", Sample.BalanceSummary)]   // hyphen stripped
    [InlineData("lop_summary", Sample.LopSummary)]           // underscore stripped
    [InlineData("LOP-SUMMARY", Sample.LopSummary)]
    public void TryParseTolerant_accepts_case_and_separator_variants(string input, Sample expected)
    {
        EnumParsing.TryParseTolerant<Sample>(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void TryParseTolerant_returns_false_for_null_blank_or_unknown(string? input)
        => EnumParsing.TryParseTolerant<Sample>(input, out _).Should().BeFalse();
}
