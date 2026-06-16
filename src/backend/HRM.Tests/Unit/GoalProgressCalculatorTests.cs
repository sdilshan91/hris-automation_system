// ============================================================================
// US-PRF-009: Goal-progress pure-math unit tests (GoalProgressCalculator).
//
// Covers FR-4 weighted overall completion across varying weights/progress and the AC-5/FR-6/BR-4 staleness gate
// (including the BR-4 "0 disables" rule and the no-update-yet path). Pure, framework-free — no DbContext.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Performance;

namespace HRM.Tests.Unit;

public sealed class GoalProgressCalculatorTests
{
    // ── FR-4: weighted overall completion ───────────────────────────────

    [Fact]
    public void WeightedOverallCompletion_empty_is_zero()
        => GoalProgressCalculator.WeightedOverallCompletion([]).Should().Be(0);

    [Fact]
    public void WeightedOverallCompletion_weights_the_average_by_goal_weight()
    {
        // 50%*100 + 30%*50 + 20%*0 = 50 + 15 + 0 = 65 over total weight 100.
        var goals = new[] { (Weight: 50, LatestProgressPct: 100), (30, 50), (20, 0) };
        GoalProgressCalculator.WeightedOverallCompletion(goals).Should().Be(65);
    }

    [Fact]
    public void WeightedOverallCompletion_equal_weights_is_arithmetic_mean()
    {
        var goals = new[] { (Weight: 25, LatestProgressPct: 80), (25, 40), (25, 20), (25, 60) };
        GoalProgressCalculator.WeightedOverallCompletion(goals).Should().Be(50);
    }

    [Fact]
    public void WeightedOverallCompletion_a_heavier_goal_dominates()
    {
        // 90%*100 + 10%*0 = 90.
        var goals = new[] { (Weight: 90, LatestProgressPct: 100), (10, 0) };
        GoalProgressCalculator.WeightedOverallCompletion(goals).Should().Be(90);
    }

    [Fact]
    public void WeightedOverallCompletion_zero_total_weight_falls_back_to_mean()
    {
        var goals = new[] { (Weight: 0, LatestProgressPct: 100), (0, 0) };
        GoalProgressCalculator.WeightedOverallCompletion(goals).Should().Be(50);
    }

    [Fact]
    public void WeightedOverallCompletion_clamps_out_of_range_progress()
    {
        var goals = new[] { (Weight: 100, LatestProgressPct: 250) };
        GoalProgressCalculator.WeightedOverallCompletion(goals).Should().Be(100);
    }

    // ── AC-5/FR-6/BR-4: staleness ───────────────────────────────────────

    private static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsStale_recent_update_is_not_stale()
        => GoalProgressCalculator.IsStale(Now.AddDays(-5), Now.AddDays(-40), 14, Now).Should().BeFalse();

    [Fact]
    public void IsStale_old_update_is_stale()
        => GoalProgressCalculator.IsStale(Now.AddDays(-20), Now.AddDays(-40), 14, Now).Should().BeTrue();

    [Fact]
    public void IsStale_no_update_measured_from_since()
    {
        // No update; goal-setting closed 20 days ago, interval 14 ⇒ stale.
        GoalProgressCalculator.IsStale(null, Now.AddDays(-20), 14, Now).Should().BeTrue();
        // No update; only 10 days since close ⇒ not yet stale.
        GoalProgressCalculator.IsStale(null, Now.AddDays(-10), 14, Now).Should().BeFalse();
    }

    [Fact]
    public void IsStale_zero_days_disables_the_sweep()
    {
        // BR-4: 0 disables nudges — never stale even with a very old update.
        GoalProgressCalculator.IsStale(Now.AddDays(-100), Now.AddDays(-100), 0, Now).Should().BeFalse();
        GoalProgressCalculator.IsStale(null, Now.AddDays(-100), 0, Now).Should().BeFalse();
    }
}
