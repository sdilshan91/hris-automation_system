using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;

namespace HRM.Api.HostedServices;

/// <summary>
/// US-PLT-004 — the background flusher for the in-memory <see cref="IApiCallCounter"/>. On a fixed interval (and
/// once more on graceful shutdown) it drains the buffered per-tenant increments and UPSERTs them into
/// <c>tenant_api_usage</c> via <see cref="TenantApiCallUsage.UpsertAsync"/> (atomic <c>call_count = call_count +
/// n</c>). Keeping the write off the request path is the whole point: requests only touch memory.
///
/// <para><b>Cross-tenant, privileged path.</b> The flusher owns a scope with NO resolved tenant, so under RLS it
/// routes to the BYPASSRLS (<c>hrm_owner</c>) connection — one writer can span every tenant's row, exactly like
/// the monitoring service's cross-tenant reads. Today (RLS dormant) it simply runs on the default connection.</para>
///
/// <para><b>Fail-safe.</b> A flush failure re-buffers its drained deltas (so counts are retried, not lost) and is
/// logged, never fatal. On ungraceful shutdown the un-flushed buffer is lost — an accepted trade for a usage
/// meter (see <see cref="IApiCallCounter"/>). Gate: <c>ApiCallCounter:Enabled</c> (default true).</para>
/// </summary>
public sealed class ApiCallCounterFlushService : BackgroundService
{
    private readonly IApiCallCounter _counter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiCallCounterFlushService> _logger;

    public ApiCallCounterFlushService(
        IApiCallCounter counter,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ApiCallCounterFlushService> logger)
    {
        _counter = counter;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private TimeSpan FlushInterval =>
        TimeSpan.FromSeconds(Math.Max(1, _configuration.GetValue("ApiCallCounter:FlushIntervalSeconds", 10)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("ApiCallCounter:Enabled", true))
        {
            _logger.LogInformation("API-call counter flusher disabled (ApiCallCounter:Enabled=false); skipping.");
            return;
        }

        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — fall through to the graceful final flush below.
        }

        // Graceful shutdown: persist whatever is still buffered (best-effort, uncancellable).
        await FlushAsync(CancellationToken.None);
    }

    /// <summary>
    /// Drains the buffer and upserts the deltas. Public so an integration test can trigger a deterministic flush
    /// without waiting for the timer. Never throws: on failure it re-buffers the drained deltas and logs.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var deltas = _counter.Drain();
        if (deltas.Count == 0)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TenantApiCallUsage.UpsertAsync(db, deltas, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            // Put the counts back so the next tick retries them rather than dropping usage on a transient error.
            foreach (var delta in deltas)
                _counter.Add(delta);
            _logger.LogWarning(ex, "API-call usage flush failed; {Count} delta(s) re-buffered for retry.", deltas.Count);
        }
    }
}
