// ============================================================================
// BUG-291: pure unit arms for the two internal seams that make leave accrual frequency-aware —
// LeaveEntitlementService.AccrualPeriodProgress (how many periods have elapsed + periods-per-year) and
// PeriodAccrualAmount (per-period credit via cumulative-difference so the rows sum to exactly the annual
// entitlement with no numeric(5,2) drift). Framework-free, so the boundary arithmetic that is easy to get
// wrong at the period edges is proven fast + in isolation (mirrors LeaveYearBoundsTests / LeaveEntitlementEngineTests).
// The real-Postgres end-to-end ledger arms live in Integration/LeaveAccrualFrequencyPostgresTests.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using Xunit;

namespace HRM.Tests.Unit;

public sealed class LeaveAccrualPeriodMathTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    // ── periods-per-year per frequency ─────────────────────────────────────
    [Theory]
    [InlineData(AccrualFrequency.Monthly, 12)]
    [InlineData(AccrualFrequency.Quarterly, 4)]
    [InlineData(AccrualFrequency.Yearly, 1)]
    [InlineData(AccrualFrequency.Upfront, 1)]
    public void PeriodsPerYear_MatchesFrequency(AccrualFrequency freq, int expected)
    {
        var (_, periodsPerYear) = LeaveEntitlementService.AccrualPeriodProgress(freq, Jan1, new DateOnly(2026, 6, 15));
        periodsPerYear.Should().Be(expected);
    }

    // ── Monthly: one period per elapsed calendar month ─────────────────────
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(6, 6)]
    [InlineData(12, 12)]
    public void Monthly_ElapsedPeriods_TracksMonth(int month, int expectedElapsed)
    {
        var (elapsed, _) = LeaveEntitlementService.AccrualPeriodProgress(
            AccrualFrequency.Monthly, Jan1, new DateOnly(2026, month, 15));
        elapsed.Should().Be(expectedElapsed);
    }

    // ── Quarterly: months 1-3 → Q1, 4-6 → Q2, 7-9 → Q3, 10-12 → Q4 ─────────
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(7, 3)]
    [InlineData(10, 4)]
    [InlineData(12, 4)]
    public void Quarterly_ElapsedPeriods_TracksQuarter(int month, int expectedElapsed)
    {
        var (elapsed, _) = LeaveEntitlementService.AccrualPeriodProgress(
            AccrualFrequency.Quarterly, Jan1, new DateOnly(2026, month, 15));
        elapsed.Should().Be(expectedElapsed);
    }

    [Fact]
    public void BeforeYearStart_NothingElapsed()
    {
        var (elapsed, _) = LeaveEntitlementService.AccrualPeriodProgress(
            AccrualFrequency.Monthly, Jan1, new DateOnly(2025, 12, 31));
        elapsed.Should().Be(0, "the leave year has not started yet");
    }

    [Fact]
    public void AfterYearEnd_CapsAtFinalPeriod()
    {
        // Back-filling a fully-elapsed past year credits the whole year, never more.
        LeaveEntitlementService.AccrualPeriodProgress(AccrualFrequency.Monthly, Jan1, new DateOnly(2027, 5, 1))
            .ElapsedPeriods.Should().Be(12);
        LeaveEntitlementService.AccrualPeriodProgress(AccrualFrequency.Quarterly, Jan1, new DateOnly(2027, 5, 1))
            .ElapsedPeriods.Should().Be(4);
        LeaveEntitlementService.AccrualPeriodProgress(AccrualFrequency.Yearly, Jan1, new DateOnly(2027, 5, 1))
            .ElapsedPeriods.Should().Be(1);
    }

    // ── Fiscal (April-start) tenant: period math anchors on the leave-year START, not January ──────────────
    [Fact]
    public void FiscalYear_AnchorsPeriodsOnLeaveYearStart()
    {
        var apr1 = new DateOnly(2026, 4, 1);
        // First month of an April fiscal year.
        LeaveEntitlementService.AccrualPeriodProgress(AccrualFrequency.Monthly, apr1, new DateOnly(2026, 4, 10))
            .ElapsedPeriods.Should().Be(1);
        // Following January is the 10th month of the April-2026 leave year.
        LeaveEntitlementService.AccrualPeriodProgress(AccrualFrequency.Monthly, apr1, new DateOnly(2027, 1, 10))
            .ElapsedPeriods.Should().Be(10);
    }

    // ── PeriodAccrualAmount: per-period rows sum to EXACTLY the annual entitlement (no numeric(5,2) drift) ──
    [Theory]
    [InlineData(24, 12)] // clean: 2.00/month
    [InlineData(10, 12)] // 0.8333…/month → rounds per period but must still sum to 10.00
    [InlineData(12, 4)]  // 3.00/quarter
    [InlineData(7, 4)]   // 1.75/quarter
    [InlineData(20, 1)]  // yearly: one row = full entitlement
    [InlineData(15.5, 12)]
    public void PeriodAmounts_SumToAnnual_NoDrift(decimal annual, int periodsPerYear)
    {
        decimal sum = 0m;
        for (int period = 1; period <= periodsPerYear; period++)
        {
            decimal amount = LeaveEntitlementService.PeriodAccrualAmount(annual, period, periodsPerYear);
            // Every per-period amount must store exactly in numeric(5,2).
            (amount == decimal.Round(amount, 2)).Should().BeTrue($"period {period} amount {amount} must fit numeric(5,2)");
            sum += amount;
        }
        sum.Should().Be(annual, "the cumulative-difference per-period amounts sum to exactly the annual entitlement");
    }

    [Fact]
    public void PeriodAmount_SinglePeriod_IsFullEntitlement()
    {
        LeaveEntitlementService.PeriodAccrualAmount(20m, period: 1, periodsPerYear: 1).Should().Be(20m);
    }
}
