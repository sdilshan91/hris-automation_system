// ============================================================================
// US-PAY-006 FR-4: fiscal-year / effective-range resolution — unit tests (pure).
//   - IsInEffect inclusive bounds; open-ended (null effective-to).
//   - SelectEffective picks the version whose range contains the period; latest effective-from wins on overlap.
//   - No version in effect => -1.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Payroll;

namespace HRM.Tests.Unit;

public sealed class FiscalYearResolverTests
{
    [Fact]
    public void IsInEffect_WithinRange_IsTrue()
        => FiscalYearResolver.IsInEffect(new DateOnly(2026, 6, 1), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31))
            .Should().BeTrue();

    [Fact]
    public void IsInEffect_OnBounds_IsInclusive()
    {
        FiscalYearResolver.IsInEffect(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)).Should().BeTrue();
        FiscalYearResolver.IsInEffect(new DateOnly(2027, 3, 31), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)).Should().BeTrue();
    }

    [Fact]
    public void IsInEffect_BeforeStartOrAfterEnd_IsFalse()
    {
        FiscalYearResolver.IsInEffect(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)).Should().BeFalse();
        FiscalYearResolver.IsInEffect(new DateOnly(2027, 4, 1), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)).Should().BeFalse();
    }

    [Fact]
    public void IsInEffect_OpenEnded_IsTrueForAnyFutureDate()
        => FiscalYearResolver.IsInEffect(new DateOnly(2030, 1, 1), new DateOnly(2026, 4, 1), null).Should().BeTrue();

    [Fact]
    public void SelectEffective_PicksTheVersionContainingThePeriod()
    {
        var period = FiscalYearResolver.PeriodDate(2026, 6);
        var candidates = new List<(DateOnly, DateOnly?)>
        {
            (new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31)), // FY2025-2026
            (new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)), // FY2026-2027 (the period falls here)
        };
        FiscalYearResolver.SelectEffective(period, candidates).Should().Be(1);
    }

    [Fact]
    public void SelectEffective_OnOverlap_LatestEffectiveFromWins()
    {
        var period = new DateOnly(2026, 6, 1);
        var candidates = new List<(DateOnly, DateOnly?)>
        {
            (new DateOnly(2026, 1, 1), null),
            (new DateOnly(2026, 5, 1), null), // later effective-from, also in effect -> wins
        };
        FiscalYearResolver.SelectEffective(period, candidates).Should().Be(1);
    }

    [Fact]
    public void SelectEffective_NoVersionInEffect_ReturnsMinusOne()
    {
        var period = new DateOnly(2024, 6, 1);
        var candidates = new List<(DateOnly, DateOnly?)> { (new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)) };
        FiscalYearResolver.SelectEffective(period, candidates).Should().Be(-1);
    }
}
