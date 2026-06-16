using HRM.Application.Features.Monitoring.DTOs;

namespace HRM.Application.Features.Monitoring;

/// <summary>
/// Pure, side-effect-free classification helpers for US-ADM-002. Extracted from the monitoring service so the
/// band boundaries (AC-2/FR-3) and the health roll-up (AC-1) are unit-testable without a database or Hangfire.
/// </summary>
public static class MonitoringClassifiers
{
    /// <summary>
    /// Classifies a usage gauge into a band from its percent (AC-2/FR-3). Boundaries:
    /// green 0-79, amber 80-94, red 95-99, breached &gt;=100. Negative percent clamps to green.
    /// </summary>
    public static UsageBand ClassifyBand(double usagePercent) => usagePercent switch
    {
        >= 100 => UsageBand.Breached,
        >= 95 => UsageBand.Red,
        >= 80 => UsageBand.Amber,
        _ => UsageBand.Green,
    };

    /// <summary>
    /// Computes a usage percent from used/limit. Returns null when the limit is unknown (null) — an unlimited
    /// or unknown-limit plan has no gauge. A zero limit with any usage is treated as breached (100+%).
    /// </summary>
    public static double? ComputePercent(int used, int? limit)
    {
        if (limit is null) return null;
        if (limit.Value <= 0) return used > 0 ? 100d : 0d;
        return Math.Round((double)used / limit.Value * 100d, 1);
    }

    /// <summary>
    /// True when a gauge is at/over 80% — i.e. belongs in the quota-breach queue (AC-3/FR-3). Unknown-limit
    /// gauges (null percent) are never in the queue.
    /// </summary>
    public static bool IsQuotaWarning(double? usagePercent) => usagePercent is >= 80d;

    /// <summary>
    /// Rolls the real infrastructure signals up into an overall platform status (AC-1):
    /// Down if the database is unreachable; Degraded if Redis is actually DOWN OR the Hangfire failed-job count
    /// is at/above <paramref name="failedJobThreshold"/>; otherwise Healthy.
    ///
    /// <para>Redis <see cref="DependencyHealthStatus.NotConfigured"/> is deliberately NOT treated as degraded —
    /// it is a deployment fact (this platform does not run Redis), not a failure. Forcing Degraded on it would
    /// make the dashboard permanently amber and mask real outages. Only a Redis that is configured AND failing
    /// its ping (Down) degrades the roll-up.</para>
    /// </summary>
    public static PlatformHealthStatus RollUpHealth(
        DependencyHealthStatus database,
        DependencyHealthStatus redis,
        long? hangfireFailed,
        int failedJobThreshold = 50)
    {
        if (database == DependencyHealthStatus.Down)
            return PlatformHealthStatus.Down;

        var redisDegraded = redis == DependencyHealthStatus.Down;
        var jobsDegraded = hangfireFailed is { } f && f >= failedJobThreshold;

        return redisDegraded || jobsDegraded
            ? PlatformHealthStatus.Degraded
            : PlatformHealthStatus.Healthy;
    }
}
