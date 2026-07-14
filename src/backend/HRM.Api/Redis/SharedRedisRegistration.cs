using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace HRM.Api.Redis;

/// <summary>
/// Registers the ONE shared <see cref="IConnectionMultiplexer"/> that the API host routes its Redis consumers
/// through: <c>IDistributedCache</c> (<c>AddStackExchangeRedisCache</c>, wired in
/// <c>HRM.Infrastructure.DependencyInjection.AddInfrastructure</c>) and the SignalR backplane
/// (<c>AddStackExchangeRedis</c>, wired in <c>Program.cs</c>). A single shared multiplexer lets OpenTelemetry's
/// <c>AddRedisInstrumentation</c> emit command-spans for BOTH consumers (it instruments only the multiplexer
/// instance it resolves from DI) and consolidates their connection pools into one.
///
/// <para><b>Redis-gated + fail-open.</b> Returns <c>null</c> and registers nothing when no Redis connection string
/// is configured (the shipped default — dev has no Redis), so the app falls back cleanly to in-memory
/// IDistributedCache + in-memory SignalR backplane. When configured it uses <c>AbortOnConnectFail=false</c> so a
/// down/unreachable Redis never blocks or throws at startup (the app's Redis-is-never-a-hard-dependency stance).</para>
///
/// <para>Extracted as a callable extension (rather than inlined in <c>Program.cs</c>) so the DI-wiring test exercises
/// the SAME registration code the API host runs, and so <c>Program.cs</c> can capture the returned instance for the
/// SignalR <c>ConnectionFactory</c>.</para>
/// </summary>
public static class SharedRedisRegistration
{
    /// <summary>
    /// Builds the shared multiplexer (if a Redis connection string is configured) and registers it as a singleton.
    /// Call this BEFORE <c>AddInfrastructure</c> and <c>AddSignalR</c> so both can route onto the returned instance.
    /// </summary>
    /// <returns>The registered <see cref="IConnectionMultiplexer"/>, or <c>null</c> when Redis is not configured.</returns>
    public static IConnectionMultiplexer? AddSharedRedisMultiplexer(
        this IServiceCollection services, IConfiguration configuration)
    {
        var redisConn = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConn))
        {
            return null;
        }

        var opts = ConfigurationOptions.Parse(redisConn);
        opts.AbortOnConnectFail = false; // never block/throw at startup if Redis is down (Redis-optional stance)

        // BUG-115: bound the per-operation timeout so a FROZEN/unresponsive Redis (e.g. docker pause: TCP stays open
        // but unanswered, so ConnectTimeout never trips) fast-fails to the DB fallback instead of stalling the full
        // 5s SyncTimeout default per op (read + write-back = ~11s). ConnectTimeout stays from the connection string.
        var opTimeoutMs = configuration.GetValue<int?>("Redis:OperationTimeoutMs") ?? 1000;
        opts.SyncTimeout = opTimeoutMs;
        opts.AsyncTimeout = opTimeoutMs;

        // Sync Connect keeps Program.cs's main shape unchanged; with AbortOnConnectFail=false it returns immediately
        // (a disconnected multiplexer that reconnects in the background) even when Redis is unreachable.
        var multiplexer = ConnectionMultiplexer.Connect(opts);
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        return multiplexer;
    }
}
