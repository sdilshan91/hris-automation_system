using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-PLT-004 — the process-wide singleton in-memory API-call buffer (see <see cref="IApiCallCounter"/>).
///
/// <para>Each (tenant, year-month) bucket is a <see cref="StrongBox{T}"/> holding a <c>long</c> counter mutated
/// only via <see cref="Interlocked"/>, so increments are lock-free and lossless under heavy concurrency.
/// <see cref="Drain"/> uses <see cref="Interlocked.Exchange(ref long, long)"/> to read-and-zero each bucket
/// atomically, leaving the key in place — an increment racing a drain lands either in the pre- or post-zero
/// value, never in a gap.</para>
/// </summary>
public sealed class ApiCallCounter : IApiCallCounter
{
    private readonly ConcurrentDictionary<(Guid TenantId, int YearMonth), StrongBox<long>> _buckets = new();

    // TC-ADM-002-14/-16: the latency histogram, same lock-free discipline as the call counter. Keyed on the
    // truncated UTC hour so the hourly rows ARE the 24h trend — no re-bucketing at read time.
    private readonly ConcurrentDictionary<(Guid TenantId, DateTime HourUtc, int BucketIndex), StrongBox<long>> _latency = new();

    public void Increment(Guid tenantId, DateTime nowUtc)
    {
        var box = _buckets.GetOrAdd((tenantId, TenantApiUsage.ToYearMonth(nowUtc)), static _ => new StrongBox<long>(0L));
        Interlocked.Increment(ref box.Value);
    }

    public void RecordLatency(Guid tenantId, DateTime nowUtc, double elapsedMs)
    {
        // A negative/NaN elapsed can only come from a clock anomaly; clamp rather than corrupt the histogram.
        if (double.IsNaN(elapsedMs) || elapsedMs < 0d)
            elapsedMs = 0d;

        var hour = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
        var key = (tenantId, hour, LatencyBuckets.IndexFor(elapsedMs));
        var box = _latency.GetOrAdd(key, static _ => new StrongBox<long>(0L));
        Interlocked.Increment(ref box.Value);
    }

    public IReadOnlyList<LatencyBucketDelta> DrainLatency()
    {
        var deltas = new List<LatencyBucketDelta>();
        foreach (var (key, box) in _latency)
        {
            var n = Interlocked.Exchange(ref box.Value, 0L);
            if (n != 0L)
                deltas.Add(new LatencyBucketDelta(key.TenantId, key.HourUtc, key.BucketIndex, n));
        }
        return deltas;
    }

    public IReadOnlyList<ApiCallCountDelta> Drain()
    {
        var deltas = new List<ApiCallCountDelta>();
        foreach (var (key, box) in _buckets)
        {
            var n = Interlocked.Exchange(ref box.Value, 0L);
            if (n != 0L)
                deltas.Add(new ApiCallCountDelta(key.TenantId, key.YearMonth, n));
        }

        return deltas;
    }

    public void Add(ApiCallCountDelta delta)
    {
        if (delta.Count == 0L)
            return;

        var box = _buckets.GetOrAdd((delta.TenantId, delta.YearMonth), static _ => new StrongBox<long>(0L));
        Interlocked.Add(ref box.Value, delta.Count);
    }
}
