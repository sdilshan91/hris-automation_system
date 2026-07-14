// ============================================================================
// Redis command-spans (plan §5) — DI-wiring SAFETY property.
// Exercises the REAL registration code the API host runs — the shared multiplexer
// (HRM.Api SharedRedisRegistration.AddSharedRedisMultiplexer) + the Redis-gated
// IDistributedCache swap in HRM.Infrastructure.DependencyInjection.AddInfrastructure:
//   * Redis configured   -> ONE shared IConnectionMultiplexer (singleton) + RedisCache
//   * no Redis (default) -> no IConnectionMultiplexer + in-memory MemoryDistributedCache
// It does NOT connect to Redis: AbortOnConnectFail=false returns a disconnected
// multiplexer immediately, and RedisCache defers its connection until first use.
// ============================================================================

using FluentAssertions;
using HRM.Api.Redis;
using HRM.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace HRM.Tests.Unit;

public sealed class RedisWiringDiRegistrationTests
{
    // Build the container via the REAL registrations so we test the actual gate, not a copy of it. The Postgres
    // connection string is registered lazily into the DbContext options and never opened (we only resolve cache
    // services), so a bare placeholder is enough.
    private static ServiceProvider BuildProvider(string? redisConnectionString, int? operationTimeoutMs = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused",
                ["ConnectionStrings:Redis"] = redisConnectionString,
                ["Redis:OperationTimeoutMs"] = operationTimeoutMs?.ToString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        // Order mirrors Program.cs: the shared multiplexer is registered BEFORE AddInfrastructure so the
        // Redis-backed IDistributedCache can resolve it via ConnectionMultiplexerFactory.
        services.AddSharedRedisMultiplexer(configuration);
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact] // no Redis (the shipped default) → no multiplexer, in-memory distributed cache
    public void WhenRedisNotConfigured_NoMultiplexer_AndCacheIsInMemory()
    {
        using var provider = BuildProvider(redisConnectionString: "");

        provider.GetService<IConnectionMultiplexer>().Should().BeNull(
            "no Redis connection string means the shared multiplexer is not registered");
        provider.GetRequiredService<IDistributedCache>().Should().BeOfType<MemoryDistributedCache>(
            "with no Redis the cache falls back to the in-memory distributed cache");
    }

    [Fact] // Redis configured → exactly one shared multiplexer (singleton) + Redis-backed cache
    public void WhenRedisConfigured_MultiplexerIsSingleton_AndCacheIsRedisBacked()
    {
        // A resolvable-but-unused host: AbortOnConnectFail=false makes Connect return a disconnected multiplexer
        // immediately without throwing, so the test needs no live Redis and stays non-flaky.
        using var provider = BuildProvider(redisConnectionString: "localhost:6399,abortConnect=false");

        var first = provider.GetService<IConnectionMultiplexer>();
        var second = provider.GetService<IConnectionMultiplexer>();

        first.Should().NotBeNull("a Redis connection string registers the shared multiplexer");
        second.Should().BeSameAs(first, "the shared multiplexer is a singleton (one connection pool)");
        // .NET 10's cache package resolves IDistributedCache to RedisCacheImpl : RedisCache (the public base),
        // so assert the Redis-backed family rather than the exact concrete type.
        provider.GetRequiredService<IDistributedCache>().Should().BeAssignableTo<RedisCache>(
            "with Redis configured the distributed cache is the Redis-backed RedisCache");
    }

    [Fact] // BUG-115: the per-op timeout is bounded (1000ms default), not the 5000ms StackExchange.Redis default
    public void WhenRedisConfigured_PerOperationTimeoutIsBounded_NotThe5sDefault()
    {
        using var provider = BuildProvider(redisConnectionString: "localhost:6399,abortConnect=false");

        var mux = provider.GetService<IConnectionMultiplexer>();
        mux.Should().NotBeNull();

        // Round-trip the live configuration string the multiplexer was built with.
        var opts = ConfigurationOptions.Parse(mux!.Configuration);
        opts.SyncTimeout.Should().Be(1000, "a frozen Redis must fast-fail per op, not stall the 5s default");
        opts.AsyncTimeout.Should().Be(1000,
            "the IDistributedCache path is async, so AsyncTimeout is the one that governs the cached-read fast-fail");
    }

    [Fact] // BUG-115: Redis:OperationTimeoutMs overrides the bounded default
    public void WhenOperationTimeoutOverridden_ItIsHonoured()
    {
        using var provider = BuildProvider(
            redisConnectionString: "localhost:6399,abortConnect=false", operationTimeoutMs: 250);

        var mux = provider.GetService<IConnectionMultiplexer>();
        mux.Should().NotBeNull();

        var opts = ConfigurationOptions.Parse(mux!.Configuration);
        opts.SyncTimeout.Should().Be(250, "Redis:OperationTimeoutMs must override the 1000ms default");
        opts.AsyncTimeout.Should().Be(250, "Redis:OperationTimeoutMs must override the 1000ms default");
    }
}
