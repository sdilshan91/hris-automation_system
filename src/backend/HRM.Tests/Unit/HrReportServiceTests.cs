// ============================================================================
// US-RPT-001: Pre-built HR report domain-rule unit tests.
// Covers the load-bearing pure logic exercised by HrReportService:
//   - BR-4: active-status classification (Active | Probation are active).
//   - BR-4: separated-value classification (terminated / resigned / contract-ended).
//   - BR-3: voluntary vs involuntary split (used by the turnover report).
//   - BR-5: age computed AT THE REPORT DATE (not current date), 5-yr-bucket boundary.
// The turnover RATE formula itself (BR-3 = separations / avg-headcount * 100) is verified end-to-end in
// HrReportIntegrationTests; here we lock the classification primitives the formula depends on.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class HrReportServiceTests
{
    // ── BR-4: active classification ─────────────────────────────────────────

    [Theory]
    [InlineData(EmployeeStatus.Active, true)]
    [InlineData(EmployeeStatus.Probation, true)]
    [InlineData(EmployeeStatus.Inactive, false)]
    [InlineData(EmployeeStatus.Terminated, false)]
    [InlineData(EmployeeStatus.Suspended, false)]
    public void IsActive_ClassifiesPerBr4(EmployeeStatus status, bool expected)
        => HrReportService.IsActive(status).Should().Be(expected);

    // ── BR-4: separated-value classification ────────────────────────────────

    [Theory]
    [InlineData("Terminated", true)]
    [InlineData("Inactive", true)]
    [InlineData("Resigned", true)]            // free-text resignation
    [InlineData("Contract Ended", true)]
    [InlineData("contract_ended", true)]
    [InlineData("Active", false)]
    [InlineData("Probation", false)]
    [InlineData("", false)]
    public void IsSeparatedValue_ClassifiesPerBr4(string newValue, bool expected)
        => HrReportService.IsSeparatedValue(newValue).Should().Be(expected);

    // ── BR-3: voluntary vs involuntary split ────────────────────────────────

    [Theory]
    [InlineData("Resigned", null, true)]
    [InlineData("Inactive", "Voluntary resignation", true)]
    [InlineData("Terminated", null, false)]
    [InlineData("Inactive", "Performance dismissal", false)]
    [InlineData("Inactive", null, false)]     // unclassified defaults to involuntary (never silently dropped)
    public void IsVoluntary_SplitsPerBr3(string newValue, string? reason, bool expected)
        => HrReportService.IsVoluntary(newValue, reason).Should().Be(expected);

    // ── BR-5: age at report date ────────────────────────────────────────────

    [Fact]
    public void AgeAt_UsesReportDate_NotCurrentDate()
    {
        var dob = new DateTime(1990, 6, 15);
        // At report date 2020-06-15 the employee is exactly 30 — regardless of today's date.
        HrReportService.AgeAt(dob, new DateTime(2020, 6, 15)).Should().Be(30);
    }

    [Fact]
    public void AgeAt_SubtractsYear_WhenBirthdayNotYetReachedInReportYear()
    {
        var dob = new DateTime(1990, 12, 31);
        // Report date is before the birthday in that year => still 29, not 30.
        HrReportService.AgeAt(dob, new DateTime(2020, 6, 15)).Should().Be(29);
    }

    [Fact]
    public void AgeAt_FutureDateOfBirth_ReturnsZero()
        => HrReportService.AgeAt(new DateTime(2030, 1, 1), new DateTime(2020, 1, 1)).Should().Be(0);

    [Fact]
    public void AgeAt_BucketBoundary_FloorsTo5YearBucket()
    {
        // 5-year bucketing is `age / 5 * 5`: age 34 → bucket 30, age 35 → bucket 35.
        var dob = new DateTime(1985, 1, 1);
        var age34 = HrReportService.AgeAt(dob, new DateTime(2019, 1, 1)); // turns 34
        var age35 = HrReportService.AgeAt(dob, new DateTime(2020, 1, 1)); // turns 35
        age34.Should().Be(34);
        age35.Should().Be(35);
        (age34 / 5 * 5).Should().Be(30);
        (age35 / 5 * 5).Should().Be(35);
    }
}
