namespace HRM.Application.Common.Interfaces;

/// <summary>One grouped error for the platform dashboard's top-errors panel (US-ADM-002 FR-6 / TC-ADM-002-16).</summary>
/// <param name="Title">The issue title as GlitchTip groups it.</param>
/// <param name="Count">Times seen.</param>
/// <param name="Level">error / warning / fatal.</param>
/// <param name="LastSeenUtc">Most recent occurrence.</param>
public sealed record TopErrorDto(string Title, long Count, string Level, DateTime? LastSeenUtc);

/// <summary>One hourly point on the 24h error-rate trend (TC-ADM-002-16).</summary>
public sealed record ErrorRatePointDto(DateTime IntervalUtc, long Count);

/// <summary>
/// Read-only view over the self-hosted GlitchTip instance's Sentry-compatible API, for the monitoring KPIs that
/// would otherwise have needed a metrics store (TC-ADM-002-14 error-rate, -15 attention queue, -16 top-errors).
///
/// <para><b>Validated against the live instance 2026-08-04</b>, not assumed: <c>stats_v2</c> returns hourly
/// interval buckets, <c>/issues/</c> carries count/level/lastSeen, and — the part that mattered —
/// <c>?query=tenant_id:&lt;id&gt;</c> genuinely DISCRIMINATES (a nonsense tenant returned 0 issues where an
/// unfiltered call returned 6). An ignored filter would also have returned HTTP 200, so the status code alone
/// proved nothing.</para>
///
/// <para><b>Every method fails SOFT.</b> A monitoring read must never break the dashboard it feeds, and
/// GlitchTip is an optional, separately-deployed component: no token configured, instance down, or an
/// unexpected payload all yield null/empty rather than throwing. Null means "not available" — the same honest
/// placeholder the SLA uptime field uses — and is never rendered as zero errors, which would read as healthy.</para>
/// </summary>
public interface IGlitchTipMetricsClient
{
    /// <summary>True when a token and org are configured — i.e. these reads can be attempted at all.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Hourly error counts over the window for one tenant (null tenant = platform-wide). Empty list when
    /// unavailable — callers must not treat that as "zero errors".
    /// </summary>
    Task<IReadOnlyList<ErrorRatePointDto>> GetErrorTrendAsync(
        Guid? tenantId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    /// <summary>Most-frequent grouped errors for one tenant (null tenant = platform-wide). Empty when unavailable.</summary>
    Task<IReadOnlyList<TopErrorDto>> GetTopErrorsAsync(
        Guid? tenantId, int limit = 10, CancellationToken cancellationToken = default);
}
