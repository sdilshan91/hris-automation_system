// ============================================================================
// US-REC-009: pure recruitment-analytics calculation helpers — unit tests.
// Covers the formulas the dashboard relies on: BR-3 funnel conversion, BR-2 offer
// acceptance, BR-1 time-to-hire (single + average), per-source hire conversion, and
// the trend-bucket labelling — all with no DbContext.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using Xunit;

namespace HRM.Tests.Unit;

public sealed class RecruitmentAnalyticsTests
{
    // ── BR-3 funnel conversion ───────────────────────────────────────

    [Theory]
    [InlineData(60, 100, 60.0)]   // 100 Applied -> 60 Screening = 60%
    [InlineData(30, 60, 50.0)]    // 60 Screening -> 30 Interview = 50%
    [InlineData(5, 10, 50.0)]
    [InlineData(0, 10, 0.0)]
    public void ConversionRate_computes_adjacent_stage_percentage(int current, int previous, decimal expected)
    {
        RecruitmentAnalytics.ConversionRate(current, previous).Should().Be(expected);
    }

    [Fact]
    public void ConversionRate_is_null_when_previous_stage_empty()
    {
        RecruitmentAnalytics.ConversionRate(5, 0).Should().BeNull();
    }

    // ── BR-2 offer acceptance ────────────────────────────────────────

    [Fact]
    public void AcceptanceRate_seven_of_ten_is_seventy_percent()
    {
        RecruitmentAnalytics.AcceptanceRate(accepted: 7, sent: 10).Should().Be(70m);
    }

    [Fact]
    public void AcceptanceRate_is_zero_when_no_offers_sent()
    {
        RecruitmentAnalytics.AcceptanceRate(accepted: 0, sent: 0).Should().Be(0m);
    }

    [Fact]
    public void AcceptanceRate_rounds_to_two_decimals()
    {
        // 2/3 -> 66.67
        RecruitmentAnalytics.AcceptanceRate(accepted: 2, sent: 3).Should().Be(66.67m);
    }

    // ── BR-1 time-to-hire ────────────────────────────────────────────

    [Fact]
    public void TimeToHireDays_counts_calendar_days_between_applied_and_hired()
    {
        var applied = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);
        var hired = new DateTime(2026, 4, 15, 17, 0, 0, DateTimeKind.Utc);
        RecruitmentAnalytics.TimeToHireDays(applied, hired).Should().Be(14);
    }

    [Fact]
    public void TimeToHireDays_clamps_negative_spans_to_zero()
    {
        var applied = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var hired = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        RecruitmentAnalytics.TimeToHireDays(applied, hired).Should().Be(0);
    }

    [Fact]
    public void AverageDays_means_the_set_rounded()
    {
        RecruitmentAnalytics.AverageDays(new[] { 10, 20, 30 }).Should().Be(20m);
        RecruitmentAnalytics.AverageDays(new[] { 10, 11 }).Should().Be(10.5m);
    }

    [Fact]
    public void AverageDays_is_zero_for_empty_set()
    {
        RecruitmentAnalytics.AverageDays(Array.Empty<int>()).Should().Be(0m);
    }

    // ── per-source hire conversion ───────────────────────────────────

    [Fact]
    public void SourceConversionRate_divides_hires_by_applicants()
    {
        RecruitmentAnalytics.SourceConversionRate(hires: 3, applicants: 12).Should().Be(25m);
        RecruitmentAnalytics.SourceConversionRate(hires: 0, applicants: 0).Should().Be(0m);
    }

    // ── trend bucket labelling ───────────────────────────────────────

    [Fact]
    public void TrendBucket_month_granularity_uses_year_month()
    {
        var instant = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        RecruitmentAnalytics.TrendBucket(instant, "month").Should().Be("2026-04");
    }

    [Fact]
    public void TrendBucket_week_granularity_uses_iso_week_label()
    {
        var instant = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var label = RecruitmentAnalytics.TrendBucket(instant, "week");
        label.Should().MatchRegex(@"^\d{4}-W\d{2}$");
    }
}
