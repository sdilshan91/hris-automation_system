// ============================================================================
// US-LV-008: Pure carry-forward / forfeiture / expiry math unit tests.
// Covers BR-1 (carry = MIN(unused, limit)), BR-2 (forfeiture), AC-4 (zero limit
// forfeits all), BR-6 (applicability), BR-3 (expiry date), and BR-4 FIFO remaining.
// The calculator is the single source of truth shared by the year-end job and the
// preview API, so these tests pin the contract both rely on.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class LeaveCarryForwardCalculatorTests
{
    private static LeaveType Type(
        decimal? limit = 5m, int? expiryMonths = 3, bool encashable = false,
        bool negativeAllowed = false, decimal annual = 14m, decimal? maxEncash = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Annual Leave",
            AnnualEntitlement = annual,
            CarryForwardLimit = limit,
            CarryForwardExpiryMonths = expiryMonths,
            Encashable = encashable,
            MaxEncashDays = maxEncash,
            NegativeBalanceAllowed = negativeAllowed,
        };

    // ── BR-1 / BR-2: carry = MIN(unused, limit); forfeit = unused - carry ──

    [Fact]
    public void Compute_8Unused_5Limit_Carries5_Forfeits3()
    {
        // The story's headline case (AC-2 / Test Hint).
        var outcome = LeaveCarryForwardCalculator.Compute(unusedBalance: 8m, carryForwardLimit: 5m);

        outcome.CarriedDays.Should().Be(5m);
        outcome.ForfeitedDays.Should().Be(3m);
    }

    [Fact]
    public void Compute_UnusedBelowLimit_CarriesAll_ForfeitsNothing()
    {
        var outcome = LeaveCarryForwardCalculator.Compute(unusedBalance: 3m, carryForwardLimit: 5m);

        outcome.CarriedDays.Should().Be(3m);
        outcome.ForfeitedDays.Should().Be(0m);
    }

    [Fact]
    public void Compute_ZeroLimit_ForfeitsEverything()
    {
        // AC-4: limit = 0 -> all unused is forfeited.
        var outcome = LeaveCarryForwardCalculator.Compute(unusedBalance: 6m, carryForwardLimit: 0m);

        outcome.CarriedDays.Should().Be(0m);
        outcome.ForfeitedDays.Should().Be(6m);
    }

    [Fact]
    public void Compute_NegativeBalance_CarriesNothingForfeitsNothing()
    {
        var outcome = LeaveCarryForwardCalculator.Compute(unusedBalance: -2m, carryForwardLimit: 5m);

        outcome.CarriedDays.Should().Be(0m);
        outcome.ForfeitedDays.Should().Be(0m);
    }

    // ── BR-6: applicability ─────────────────────────────────────────

    [Fact]
    public void AppliesTo_NullLimit_False()
        => LeaveCarryForwardCalculator.AppliesTo(Type(limit: null)).Should().BeFalse();

    [Fact]
    public void AppliesTo_NegativeBalanceType_False()
        => LeaveCarryForwardCalculator.AppliesTo(Type(negativeAllowed: true)).Should().BeFalse();

    [Fact]
    public void AppliesTo_ZeroEntitlementType_False()
        => LeaveCarryForwardCalculator.AppliesTo(Type(annual: 0m)).Should().BeFalse();

    [Fact]
    public void AppliesTo_ZeroLimit_True()
        // A configured 0 limit IS applicable (forfeit-all path), not skipped.
        => LeaveCarryForwardCalculator.AppliesTo(Type(limit: 0m)).Should().BeTrue();

    [Fact]
    public void AppliesTo_NormalAnnualLeave_True()
        => LeaveCarryForwardCalculator.AppliesTo(Type()).Should().BeTrue();

    // ── BR-3: expiry date ───────────────────────────────────────────

    [Fact]
    public void ComputeExpiryDate_3Months_FromNewYearStart()
    {
        var expiry = LeaveCarryForwardCalculator.ComputeExpiryDate(toYear: 2027, carryForwardExpiryMonths: 3);
        expiry.Should().Be(new DateOnly(2027, 4, 1));
    }

    [Fact]
    public void ComputeExpiryDate_NullMonths_NeverExpires()
    {
        var expiry = LeaveCarryForwardCalculator.ComputeExpiryDate(toYear: 2027, carryForwardExpiryMonths: null);
        expiry.Should().BeNull();
    }

    // ── BR-4: FIFO remaining ────────────────────────────────────────

    [Fact]
    public void RemainingCarriedDays_5Carried_3Used_Leaves2()
    {
        // Test Hint: employee uses 3 days after carry-forward of 5 -> 2 carried remain.
        var remaining = LeaveCarryForwardCalculator.RemainingCarriedDays(
            carriedDays: 5m, usedDaysInNewYear: 3m, alreadyExpiredDays: 0m);

        remaining.Should().Be(2m);
    }

    [Fact]
    public void RemainingCarriedDays_UsageExceedsCarried_LeavesZero()
    {
        // Used more than the carried bucket -> carried fully consumed (FIFO), nothing to expire.
        var remaining = LeaveCarryForwardCalculator.RemainingCarriedDays(
            carriedDays: 5m, usedDaysInNewYear: 8m, alreadyExpiredDays: 0m);

        remaining.Should().Be(0m);
    }

    [Fact]
    public void RemainingCarriedDays_AccountsForAlreadyExpired_Idempotent()
    {
        // A prior expiry run already expired 2 of the 5 carried; 0 used -> 3 remain.
        var remaining = LeaveCarryForwardCalculator.RemainingCarriedDays(
            carriedDays: 5m, usedDaysInNewYear: 0m, alreadyExpiredDays: 2m);

        remaining.Should().Be(3m);
    }
}
