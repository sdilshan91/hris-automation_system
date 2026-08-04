using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Persistence;

/// <summary>
/// TC-ADM-002-14/-16: persistence + read helpers for the per-tenant hourly latency histogram. Mirrors
/// <see cref="TenantApiCallUsage"/> — atomic <c>count = count + n</c> upserts, never read-modify-write, so many
/// concurrent flushes converge on one correct row per (tenant, hour, bucket).
/// </summary>
public static class TenantLatencyUsage
{
    /// <summary>Atomically folds drained deltas into the histogram.</summary>
    public static async Task UpsertAsync(
        AppDbContext db, IReadOnlyCollection<LatencyBucketDelta> deltas, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        foreach (var d in deltas)
        {
            if (d.Count == 0L)
                continue;

            var id = BaseEntity.NewUuidV7();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tenant_latency_bucket (id, tenant_id, hour_utc, bucket_index, count, created_at, is_deleted)
                VALUES ({id}, {d.TenantId}, {d.HourUtc}, {d.BucketIndex}, {d.Count}, {nowUtc}, false)
                ON CONFLICT (tenant_id, hour_utc, bucket_index)
                DO UPDATE SET count = tenant_latency_bucket.count + EXCLUDED.count,
                              updated_at = {nowUtc}
                """, cancellationToken);
        }
    }

    /// <summary>Deletes histogram rows older than the retention window.</summary>
    public static Task<int> PruneAsync(
        AppDbContext db, DateTime cutoffUtc, CancellationToken cancellationToken = default)
        => db.TenantLatencyBuckets.IgnoreQueryFilters()
            .Where(b => b.HourUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Interpolated P95 over the window for one tenant, in milliseconds — or <c>null</c> when the window holds
    /// no requests.
    ///
    /// <para><b>Null, never 0, on an empty window.</b> Zero would render as a perfectly fast service; "not
    /// available" is the only honest answer when nothing was measured — the same rule the SLA uptime field
    /// follows.</para>
    ///
    /// <para>The value is interpolated within the containing bucket (the standard histogram-quantile approach,
    /// as Prometheus does it), so it is an ESTIMATE bounded by that bucket's width — not a exact percentile.
    /// A P95 landing in the overflow bucket has no upper bound to interpolate against, so the last finite bound
    /// is reported as a floor rather than inventing a ceiling.</para>
    /// </summary>
    public static async Task<double?> P95Async(
        AppDbContext db, Guid tenantId, DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var rows = await db.TenantLatencyBuckets.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.HourUtc >= sinceUtc)
            .GroupBy(b => b.BucketIndex)
            .Select(g => new { Index = g.Key, Count = g.Sum(x => x.Count) })
            .ToListAsync(cancellationToken);

        return P95From(rows.ToDictionary(r => r.Index, r => r.Count));
    }

    /// <summary>The pure quantile rule, separated so it can be exercised directly rather than re-implemented.</summary>
    internal static double? P95From(IReadOnlyDictionary<int, long> countsByBucket)
    {
        long total = 0;
        foreach (var c in countsByBucket.Values) total += c;
        if (total == 0) return null;

        var target = total * 0.95d;
        long cumulative = 0;
        var bounds = LatencyBuckets.UpperBoundsMs;

        for (var i = 0; i < LatencyBuckets.Count; i++)
        {
            var inBucket = countsByBucket.GetValueOrDefault(i);
            if (inBucket == 0) continue;

            var previousCumulative = cumulative;
            cumulative += inBucket;
            if (cumulative < target) continue;

            // Overflow bucket: no upper bound exists, so report the last finite bound as a floor rather than
            // fabricating a ceiling.
            if (i >= bounds.Length)
                return bounds[^1];

            var lower = i == 0 ? 0d : bounds[i - 1];
            var upper = bounds[i];
            var within = (target - previousCumulative) / inBucket; // 0..1 through this bucket
            return Math.Round(lower + (upper - lower) * within, 2);
        }

        return bounds[^1];
    }

    /// <summary>Total requests observed for a tenant in the window — the error-RATE denominator (TC-ADM-002-14).</summary>
    public static async Task<long> RequestTotalAsync(
        AppDbContext db, Guid? tenantId, DateTime sinceUtc, CancellationToken cancellationToken = default)
        => await db.TenantLatencyBuckets.IgnoreQueryFilters()
            .Where(b => b.HourUtc >= sinceUtc && (tenantId == null || b.TenantId == tenantId))
            .SumAsync(b => (long?)b.Count, cancellationToken) ?? 0L;

    /// <summary>
    /// TC-ADM-002-16: the 24h LATENCY trend — P95 per hour, oldest first, alongside that hour's request volume.
    ///
    /// <para>Deliberately P95-per-hour rather than requests-per-hour. A chart labelled "latency trend" that
    /// plotted request COUNTS would be a mislabelled number — it would rise during busy periods and fall
    /// overnight while telling you nothing about how slow the service was. Volume is still returned beside it
    /// because a percentile over 3 requests means little, and the reader deserves to see that.</para>
    ///
    /// <para>Hours with no traffic are OMITTED rather than emitted as zero: a gap means "no requests", which is
    /// a different statement from "instant responses".</para>
    /// </summary>
    public static async Task<IReadOnlyList<(DateTime HourUtc, double? P95Ms, long Requests)>> HourlyLatencyAsync(
        AppDbContext db, Guid tenantId, DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var rows = await db.TenantLatencyBuckets.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.HourUtc >= sinceUtc)
            .Select(b => new { b.HourUtc, b.BucketIndex, b.Count })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.HourUtc)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var byBucket = g.GroupBy(x => x.BucketIndex)
                    .ToDictionary(x => x.Key, x => x.Sum(v => v.Count));
                return (g.Key, P95From(byBucket), byBucket.Values.Sum());
            })
            .ToList();
    }
}
