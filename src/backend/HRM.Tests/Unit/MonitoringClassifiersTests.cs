// ============================================================================
// US-ADM-002: unit tests for the pure monitoring classifiers — the usage-band boundaries (AC-2/FR-3) and the
// platform-health roll-up (AC-1). These are extracted as side-effect-free pure functions so the boundaries
// (79/80/94/95/100) and the down/degraded/healthy roll-up are testable without a database or Hangfire.
// ============================================================================

using FluentAssertions;
using HRM.Application.Features.Monitoring;
using HRM.Application.Features.Monitoring.DTOs;

namespace HRM.Tests.Unit;

public sealed class MonitoringClassifiersTests
{
    // ── usage-band classifier boundaries (AC-2/FR-3): green 0-79, amber 80-94, red 95-99, breached >=100 ──

    [Theory]
    [InlineData(0, UsageBand.Green)]
    [InlineData(79, UsageBand.Green)]
    [InlineData(79.9, UsageBand.Green)]
    [InlineData(80, UsageBand.Amber)]
    [InlineData(94, UsageBand.Amber)]
    [InlineData(94.9, UsageBand.Amber)]
    [InlineData(95, UsageBand.Red)]
    [InlineData(99, UsageBand.Red)]
    [InlineData(99.9, UsageBand.Red)]
    [InlineData(100, UsageBand.Breached)]
    [InlineData(150, UsageBand.Breached)]
    public void ClassifyBand_AtBoundaries_ReturnsExpectedBand(double percent, UsageBand expected)
    {
        MonitoringClassifiers.ClassifyBand(percent).Should().Be(expected);
    }

    [Fact]
    public void ClassifyBand_NegativePercent_ClampsToGreen()
    {
        MonitoringClassifiers.ClassifyBand(-5).Should().Be(UsageBand.Green);
    }

    // ── percent computation ────────────────────────────────────────────────

    [Theory]
    [InlineData(4, 5, 80)]
    [InlineData(5, 5, 100)]
    [InlineData(3, 10, 30)]
    [InlineData(0, 10, 0)]
    public void ComputePercent_KnownLimit_ComputesRoundedPercent(int used, int limit, double expected)
    {
        MonitoringClassifiers.ComputePercent(used, limit).Should().Be(expected);
    }

    [Fact]
    public void ComputePercent_NullLimit_ReturnsNull()
    {
        MonitoringClassifiers.ComputePercent(10, null).Should().BeNull();
    }

    [Fact]
    public void ComputePercent_ZeroLimitWithUsage_IsBreached()
    {
        MonitoringClassifiers.ComputePercent(1, 0).Should().Be(100);
    }

    // ── quota-warning predicate (>= 80%) ───────────────────────────────────

    [Theory]
    [InlineData(79.9, false)]
    [InlineData(80.0, true)]
    [InlineData(100.0, true)]
    [InlineData(null, false)]
    public void IsQuotaWarning_AtBoundary_IsCorrect(double? percent, bool expected)
    {
        MonitoringClassifiers.IsQuotaWarning(percent).Should().Be(expected);
    }

    // ── health roll-up (AC-1): down if DB down; degraded if Redis down/not-configured or failed-jobs high ──

    [Fact]
    public void RollUp_DatabaseDown_IsDown_RegardlessOfOtherSignals()
    {
        MonitoringClassifiers.RollUpHealth(
            DependencyHealthStatus.Down, DependencyHealthStatus.Healthy, hangfireFailed: 0)
            .Should().Be(PlatformHealthStatus.Down);
    }

    [Fact]
    public void RollUp_AllHealthy_IsHealthy()
    {
        MonitoringClassifiers.RollUpHealth(
            DependencyHealthStatus.Healthy, DependencyHealthStatus.Healthy, hangfireFailed: 0)
            .Should().Be(PlatformHealthStatus.Healthy);
    }

    [Fact]
    public void RollUp_RedisNotConfigured_IsHealthy_DeploymentFactNotFailure()
    {
        // NotConfigured is "this platform does not run Redis" — a deployment fact, not an outage. It must NOT
        // force a permanently-amber dashboard; only a configured-but-failing (Down) Redis degrades the roll-up.
        MonitoringClassifiers.RollUpHealth(
            DependencyHealthStatus.Healthy, DependencyHealthStatus.NotConfigured, hangfireFailed: 0)
            .Should().Be(PlatformHealthStatus.Healthy);
    }

    [Fact]
    public void RollUp_RedisDown_IsDegraded()
    {
        MonitoringClassifiers.RollUpHealth(
            DependencyHealthStatus.Healthy, DependencyHealthStatus.Down, hangfireFailed: 0)
            .Should().Be(PlatformHealthStatus.Degraded);
    }

    [Theory]
    [InlineData(49, PlatformHealthStatus.Healthy)] // Redis healthy, under threshold ⇒ healthy
    [InlineData(50, PlatformHealthStatus.Degraded)] // at threshold ⇒ degraded
    [InlineData(120, PlatformHealthStatus.Degraded)]
    public void RollUp_FailedJobThreshold_DrivesDegraded(long failed, PlatformHealthStatus expected)
    {
        MonitoringClassifiers.RollUpHealth(
            DependencyHealthStatus.Healthy, DependencyHealthStatus.Healthy, hangfireFailed: failed)
            .Should().Be(expected);
    }

    [Fact]
    public void RollUp_NullFailedCount_DoesNotForceDegraded()
    {
        MonitoringClassifiers.RollUpHealth(
            DependencyHealthStatus.Healthy, DependencyHealthStatus.Healthy, hangfireFailed: null)
            .Should().Be(PlatformHealthStatus.Healthy);
    }
}
