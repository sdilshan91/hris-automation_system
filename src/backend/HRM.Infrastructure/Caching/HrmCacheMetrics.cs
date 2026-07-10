using System.Diagnostics.Metrics;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// P3 cache observability — OpenTelemetry metrics for the EF second-level cache. The
/// <c>EFCoreSecondLevelCacheInterceptor</c> library emits no OTel signals, so
/// <see cref="InstrumentedEFCacheServiceProvider"/> decorates its cache provider and reports every read here.
///
/// <para>The meter name is <c>HRM.Cache</c>, which is already collected by the OTel metrics pipeline's
/// <c>AddMeter("HRM.*")</c> wildcard (see <c>HRM.Api/Observability/ObservabilityExtensions.cs</c>). Only cache
/// OUTCOME + latency are recorded — cache keys and cached values are never tagged (no PII/value leakage).</para>
/// </summary>
public static class HrmCacheMetrics
{
    public const string MeterName = "HRM.Cache";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Count of second-level cache read attempts, tagged <c>cache.hit=true|false</c>.</summary>
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        name: "hrm.cache.requests",
        unit: "{request}",
        description: "EF second-level cache read attempts, tagged cache.hit=true|false.");

    /// <summary>Latency of a single cache read (GetValue), in milliseconds, tagged <c>cache.hit=true|false</c>.</summary>
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        name: "hrm.cache.operation.duration",
        unit: "ms",
        description: "Latency of an EF second-level cache read (GetValue), in milliseconds.");

    /// <summary>Records the outcome + latency of one cache read. Values/keys are never captured (masked).</summary>
    public static void RecordRead(bool hit, double elapsedMilliseconds)
    {
        var hitTag = new KeyValuePair<string, object?>("cache.hit", hit);
        Requests.Add(1, hitTag);
        Duration.Record(elapsedMilliseconds, hitTag);
    }
}
