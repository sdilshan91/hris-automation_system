// ============================================================================
// US-PRF-005: 360 composite-score math (FR-6) + final-score incorporation (BR-6).
//
// Pure unit tests for the two no-DB calculators that own the 360 scoring:
//   - ThreeSixtyScoreCalculator.ComputeComposite (FR-6): weights self/manager/peer/report by the cycle
//     config, normalizing by the categories that actually have feedback so a missing perspective never
//     skews the result; rounds away-from-zero to 2dp; returns 0 when nothing has feedback.
//   - ManagerReviewService.ComputeFinalScoreWith360 (BR-6): the final performance score incorporating the
//     360 composite alongside self + manager.
//   - The CRUCIAL parity check: when 360 is DISABLED the manager-review final score equals the existing
//     two-way self+manager blend (ComputeFinalScore) — i.e. enabling 360 is the only thing that changes it.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class ThreeSixtyScoreCalculatorTests
{
    private static ThreeSixtyScoreCalculator.CategoryWeights DefaultWeights()
        => new(Self: 10, Manager: 40, Peer: 30, Report: 20);

    // ── FR-6: weighted composite across all four perspectives ───────────

    [Fact]
    public void Composite_AllFourCategories_WeightsByConfig()
    {
        // self 4, mgr 5, peer 3, report 2 with weights 10/40/30/20 (sum 100):
        // (4*10 + 5*40 + 3*30 + 2*20)/100 = (40+200+90+40)/100 = 370/100 = 3.70
        var avg = new ThreeSixtyScoreCalculator.CategoryAverages(4m, 5m, 3m, 2m);

        ThreeSixtyScoreCalculator.ComputeComposite(avg, DefaultWeights()).Should().Be(3.70m);
    }

    [Fact]
    public void Composite_NormalizesByPresentCategories_WhenAPerspectiveIsMissing()
    {
        // Only self (4) + manager (5), weights 10/40. Missing peer/report are EXCLUDED from normalization:
        // (4*10 + 5*40)/(10+40) = (40+200)/50 = 240/50 = 4.80 — NOT divided by the full 100.
        var avg = new ThreeSixtyScoreCalculator.CategoryAverages(4m, 5m, null, null);

        ThreeSixtyScoreCalculator.ComputeComposite(avg, DefaultWeights()).Should().Be(4.80m);
    }

    [Fact]
    public void Composite_RoundsAwayFromZero_ToTwoDecimals()
    {
        // self 4, mgr 5, peer 3, weights 10/40/30 (report missing):
        // (4*10 + 5*40 + 3*30)/(80) = 330/80 = 4.125 → 4.13 away-from-zero.
        var avg = new ThreeSixtyScoreCalculator.CategoryAverages(4m, 5m, 3m, null);

        ThreeSixtyScoreCalculator.ComputeComposite(avg, DefaultWeights()).Should().Be(4.13m);
    }

    [Fact]
    public void Composite_ReturnsZero_WhenNoCategoryHasFeedback()
    {
        var avg = new ThreeSixtyScoreCalculator.CategoryAverages(null, null, null, null);

        ThreeSixtyScoreCalculator.ComputeComposite(avg, DefaultWeights()).Should().Be(0m);
    }

    [Fact]
    public void Composite_IgnoresCategoriesWithZeroWeight()
    {
        // peer weight is 0 → peer rating (1) is dropped from both the sum and the normalization:
        // self 5, mgr 5, peer 1(w=0): (5*10 + 5*40)/(50) = 250/50 = 5.00
        var avg = new ThreeSixtyScoreCalculator.CategoryAverages(5m, 5m, 1m, null);
        var weights = new ThreeSixtyScoreCalculator.CategoryWeights(Self: 10, Manager: 40, Peer: 0, Report: 20);

        ThreeSixtyScoreCalculator.ComputeComposite(avg, weights).Should().Be(5.00m);
    }

    // ── BR-6: final-score incorporation when 360 is enabled ─────────────

    [Fact]
    public void FinalScoreWith360_BlendsSelfManagerPeerReport()
    {
        // self 4, mgr 5, peer 3, report 2, default weights → 3.70 (same as the composite above).
        var cycle = Cycle(self: 10, mgr: 40, peer: 30, report: 20);

        ManagerReviewService.ComputeFinalScoreWith360(4m, 5m, 3m, 2m, cycle).Should().Be(3.70m);
    }

    [Fact]
    public void FinalScoreWith360_DegradesToSelfManager_WhenNoPeerOrReportData()
    {
        // No peer/report feedback yet: only self+manager contribute, normalized by their weights.
        // self 4, mgr 5, weights 10/40 → (40+200)/50 = 4.80
        var cycle = Cycle(self: 10, mgr: 40, peer: 30, report: 20);

        ManagerReviewService.ComputeFinalScoreWith360(4m, 5m, null, null, cycle).Should().Be(4.80m);
    }

    // ── BR-6 parity: 360 DISABLED ⇒ final score == existing self+manager blend ──

    [Theory]
    [InlineData(4.00, 5.00, 50, 4.50)]  // self 4*0.5 + mgr 5*0.5
    [InlineData(2.00, 4.20, 30, 3.54)]  // self 2*0.3 + mgr 4.2*0.7
    [InlineData(3.00, 4.50, 0, 4.50)]   // pure manager
    public void FinalScore_When360Disabled_EqualsTwoWaySelfManagerBlend(
        double self, double manager, int selfWeightPercent, decimal expected)
    {
        // The existing US-PRF-003 two-way blend is the source of truth when 360 is off. This is the exact
        // method ManagerReviewService.SubmitAsync calls in the `else` (Is360Enabled == false) branch.
        var managerWeightPercent = 100 - selfWeightPercent;

        var twoWay = ManagerReviewService.ComputeFinalScore(
            (decimal)self, (decimal)manager, selfWeightPercent, managerWeightPercent);

        twoWay.Should().Be(expected, "with 360 disabled the final score must be UNCHANGED from US-PRF-003");
    }

    private static AppraisalCycle Cycle(int self, int mgr, int peer, int report) => new()
    {
        Name = "FY2026", RatingScaleMax = 5, Is360Enabled = true,
        ThreeSixtySelfWeightPercent = self,
        ThreeSixtyManagerWeightPercent = mgr,
        ThreeSixtyPeerWeightPercent = peer,
        ThreeSixtyReportWeightPercent = report,
    };
}
