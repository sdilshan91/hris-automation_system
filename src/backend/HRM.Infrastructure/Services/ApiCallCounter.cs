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

    public void Increment(Guid tenantId, DateTime nowUtc)
    {
        var box = _buckets.GetOrAdd((tenantId, TenantApiUsage.ToYearMonth(nowUtc)), static _ => new StrongBox<long>(0L));
        Interlocked.Increment(ref box.Value);
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
