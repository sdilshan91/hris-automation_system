namespace HRM.Domain.Entities;

/// <summary>
/// US-PLT-004 (deferred slice): the per-tenant monthly API-call aggregate — ONE row per
/// (<see cref="BaseEntity.TenantId"/>, <see cref="YearMonth"/>) carrying a running <see cref="CallCount"/>.
///
/// <para>This is the durable landing spot the OTel request counter lacked: an OpenTelemetry counter is not
/// queryable by <c>PlatformMonitoringService</c>, so <c>max_api_calls_per_month</c> could never be reported.
/// The <c>ApiCallCounterMiddleware</c> buffers per-request increments in memory and a background flusher
/// UPSERTs them here with an atomic <c>call_count = call_count + n</c> (never read-modify-write), so many
/// concurrent requests across many tenants converge on one correct row per tenant-month.</para>
///
/// <para><b>Tenant-scoped:</b> carries a non-null <c>tenant_id</c>, an EF global query filter, and a dormant
/// <c>tenant_isolation</c> RLS policy (shipped in the migration) — so it is isolated in all three layers like
/// every other tenant entity.</para>
/// </summary>
public sealed class TenantApiUsage : BaseEntity
{
    /// <summary>
    /// The UTC calendar month this bucket counts, encoded as <c>year * 100 + month</c> (e.g. July 2026 ⇒
    /// 202607). A single integer keeps the (tenant_id, year_month) unique key cheap and index-friendly.
    /// </summary>
    public int YearMonth { get; set; }

    /// <summary>The running count of billable API requests for this tenant in this month. <c>long</c> because a
    /// busy tenant can exceed <see cref="int"/> over a month.</summary>
    public long CallCount { get; set; }

    /// <summary>Encodes a UTC instant into the <see cref="YearMonth"/> bucket key.</summary>
    public static int ToYearMonth(DateTime utc) => utc.Year * 100 + utc.Month;
}
