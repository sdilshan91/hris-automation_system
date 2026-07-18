// ============================================================================
// Unit tests for EmploymentTypeParsing — the shared EmploymentType parser extracted from the identical
// private TryParseEmploymentType helpers in the Leave and Payroll report services (reusability refactor).
// Behaviour must match the pre-extraction private copy: tolerates case, hyphens and spaces.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Helpers;
using HRM.Domain.Enums;
using Xunit;

namespace HRM.Tests.Unit;

public sealed class EmploymentTypeParsingTests
{
    [Theory]
    [InlineData("FullTime", EmploymentType.FullTime)]
    [InlineData("fulltime", EmploymentType.FullTime)]
    [InlineData("full-time", EmploymentType.FullTime)]
    [InlineData("full time", EmploymentType.FullTime)]
    [InlineData("PART-TIME", EmploymentType.PartTime)]
    [InlineData("contract", EmploymentType.Contract)]
    [InlineData("Intern", EmploymentType.Intern)]
    public void TryParse_accepts_case_hyphen_and_space_variants(string input, EmploymentType expected)
    {
        EmploymentTypeParsing.TryParse(input, out var type).Should().BeTrue();
        type.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("seasonal")]
    [InlineData("full_time")]   // underscore is NOT stripped (only '-' and space) — matches original behaviour
    public void TryParse_returns_false_for_null_blank_or_unrecognized(string? input)
        => EmploymentTypeParsing.TryParse(input, out _).Should().BeFalse();
}
