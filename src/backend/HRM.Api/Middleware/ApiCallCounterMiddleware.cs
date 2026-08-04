using System.Diagnostics;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;

namespace HRM.Api.Middleware;

/// <summary>
/// US-PLT-004 — the per-request API-call meter. For a resolved, non-system tenant hitting a metered tenant API
/// path it records ONE increment into the in-memory <see cref="IApiCallCounter"/> (a process-wide singleton); a
/// background flusher later persists the buffered counts to <c>tenant_api_usage</c>. The request path itself
/// never touches the database, so this adds no per-request DB latency on the hot path.
///
/// <para><b>Skips the same paths the module-entitlement gate skips</b> (via the shared
/// <see cref="PlatformApiPaths.IsMeteredTenantApiPath"/>): non-<c>/api/</c> paths (health probes, swagger,
/// hangfire) and the platform allow-list (auth, FE bootstrap, notifications, system-admin). Counting a health
/// probe or a login call against a customer's <c>max_api_calls_per_month</c> would look like a billing bug.</para>
///
/// <para><b>Fail-open by construction.</b> A usage meter must never be able to take the API down: the whole
/// increment is wrapped so ANY exception (a bug in the counter, an unexpected null) is swallowed and logged —
/// the request always proceeds. Slotted after tenant resolution / status / entitlement, before the controllers.</para>
/// </summary>
public sealed class ApiCallCounterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IApiCallCounter _counter;
    private readonly ILogger<ApiCallCounterMiddleware> _logger;

    public ApiCallCounterMiddleware(
        RequestDelegate next, IApiCallCounter counter, ILogger<ApiCallCounterMiddleware> logger)
    {
        _next = next;
        _counter = counter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        TryCount(context);

        // TC-ADM-002-14/-16: measure the downstream pipeline. Timestamp arithmetic only — no allocation, no
        // Stopwatch object, nothing on the DB path. The measurement must NEVER change control flow, so the
        // request is awaited exactly as before and the recording happens in a finally that cannot throw out.
        var startTs = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            TryRecordLatency(context, Stopwatch.GetElapsedTime(startTs).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Fail-open by construction, exactly like <see cref="TryCount"/>: ANY exception is swallowed and logged.
    /// A usage meter must never be able to fail a request — and this one runs in a `finally`, where a throw
    /// would replace the real response (or the real exception) with the meter's own bug.
    /// </summary>
    private void TryRecordLatency(HttpContext context, double elapsedMs)
    {
        try
        {
            if (!PlatformApiPaths.IsMeteredTenantApiPath(context.Request.Path))
                return;

            var tenant = context.RequestServices.GetService<ITenantContext>();
            if (tenant is not { IsResolved: true, IsSystemContext: false })
                return;

            _counter.RecordLatency(tenant.TenantId, DateTime.UtcNow, elapsedMs);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ApiLatency] failed to record request latency; request unaffected");
        }
    }

    // Never throws: a metering failure must not fail the request (fail-open). Kept separate from the pipeline
    // continuation so the try/catch cannot accidentally swallow a downstream error.
    private void TryCount(HttpContext context)
    {
        try
        {
            var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();

            // Only a resolved, non-system tenant has a bucket to count into.
            if (!tenantContext.IsResolved || tenantContext.IsSystemContext)
                return;

            var path = context.Request.Path.Value ?? string.Empty;
            if (!PlatformApiPaths.IsMeteredTenantApiPath(path))
                return;

            _counter.Increment(tenantContext.TenantId, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            // Fail-open: log and move on. The meter is advisory; the request must not pay for its failure.
            _logger.LogWarning(ex, "API-call counter increment failed; request proceeds un-metered.");
        }
    }
}
