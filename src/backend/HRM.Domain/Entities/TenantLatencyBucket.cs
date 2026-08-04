namespace HRM.Domain.Entities;

/// <summary>
/// TC-ADM-002-14 / -16: the per-tenant hourly request-latency histogram — ONE row per
/// (<see cref="BaseEntity.TenantId"/>, <see cref="HourUtc"/>, <see cref="BucketIndex"/>) carrying a running
/// <see cref="Count"/>.
///
/// <para>Exists because GlitchTip cannot answer latency questions (its <c>category=transaction</c> gives
/// COUNTS, and the Sentry performance endpoints are absent), and standing up a metrics store for two dashboard
/// fields would be disproportionate. The <c>ApiCallCounterMiddleware</c> already runs on every metered tenant
/// request with an in-memory buffer and a background flusher, so this reuses that proven path rather than
/// adding infrastructure.</para>
///
/// <para><b>Why a histogram rather than raw durations:</b> storing every request's duration would be unbounded;
/// fixed buckets give a bounded row count (tenants × hours × buckets) and still support an interpolated P95 —
/// the same trade-off Prometheus histograms make. The bucket bounds are FIXED (see <c>LatencyBuckets</c>)
/// because a histogram whose buckets move cannot be summed across time.</para>
///
/// <para>This table also supplies the request-total DENOMINATOR that <c>AggregateErrorRatePercent</c> needs:
/// summing <see cref="Count"/> over an hour gives the request volume the GlitchTip error count divides into.</para>
///
/// <para><b>Tenant-scoped:</b> non-null <c>tenant_id</c>, EF global query filter and a dormant
/// <c>tenant_isolation</c> RLS policy, exactly like <c>tenant_api_usage</c>.</para>
/// </summary>
public sealed class TenantLatencyBucket : BaseEntity
{
    /// <summary>The UTC hour this histogram covers, truncated to the hour.</summary>
    public DateTime HourUtc { get; set; }

    /// <summary>Index into <c>LatencyBuckets.UpperBoundsMs</c>; the last index is the overflow bucket.</summary>
    public int BucketIndex { get; set; }

    /// <summary>Requests observed in this bucket. <c>long</c> — a busy tenant exceeds int over an hour.</summary>
    public long Count { get; set; }
}
