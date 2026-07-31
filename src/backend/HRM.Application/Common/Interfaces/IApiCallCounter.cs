namespace HRM.Application.Common.Interfaces;

/// <summary>
/// A pending increment for one (tenant, year-month) bucket, produced by <see cref="IApiCallCounter.Drain"/>.
/// <paramref name="YearMonth"/> is encoded <c>year * 100 + month</c> (e.g. 202607).
/// </summary>
public readonly record struct ApiCallCountDelta(Guid TenantId, int YearMonth, long Count);

/// <summary>
/// US-PLT-004 — the in-memory, hot-path API-call counter. A process-wide SINGLETON that buffers per-request
/// increments (keyed by tenant + UTC month) so the request path never touches the database; a background
/// flusher periodically <see cref="Drain"/>s the buffer and UPSERTs the deltas into <c>tenant_api_usage</c>
/// with atomic <c>call_count = call_count + n</c> arithmetic.
///
/// <para><b>Failure mode (accepted):</b> increments accumulated since the last flush live only in memory, so an
/// ungraceful shutdown (SIGKILL / crash / power loss) loses up to one flush-interval of counts. This is
/// deliberate: the counter is an advisory usage METER (it feeds a monitoring gauge; enforcement is out of scope
/// for this slice), so trading strict durability for zero per-request DB latency is the right call. A graceful
/// shutdown flushes the residual buffer first, so only hard kills lose data.</para>
/// </summary>
public interface IApiCallCounter
{
    /// <summary>Records one API call for <paramref name="tenantId"/> in the UTC month of <paramref name="nowUtc"/>.
    /// Lock-free and safe under high concurrency.</summary>
    void Increment(Guid tenantId, DateTime nowUtc);

    /// <summary>
    /// Atomically reads-and-zeroes every non-empty bucket, returning the deltas to flush. Increments that arrive
    /// during a drain are never lost — they land in the freshly-zeroed bucket and surface on the next drain.
    /// </summary>
    IReadOnlyList<ApiCallCountDelta> Drain();

    /// <summary>Re-buffers a delta (used to return un-flushed counts to the buffer when a flush fails), so a
    /// transient DB error costs a retry, not lost usage.</summary>
    void Add(ApiCallCountDelta delta);
}
