// ============================================================================
// DF-7 (US-REC-008 NFR-6, ISSUE-130): RedisPortalLinkIpRateLimiter — the
// distributed, multi-instance-safe per-IP applicant-portal magic-link throttle.
// Proves the fixed-window INCR+EXPIRE counter: first N acquire (true), the
// (N+1)th blocks (false), a different (tenant,IP) key is independent, EXPIRE is
// stamped on the FIRST hit only, and a Redis blip FAILS OPEN (allow).
//
// Redis is faked via NSubstitute over IDatabase/IConnectionMultiplexer with a
// stateful dictionary backing store so INCR round-trips authentically (the same
// thin-seam pattern RedisPermissionCacheTests uses — no Docker/Testcontainer).
// ============================================================================

using FluentAssertions;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace HRM.Tests.Unit;

public sealed class RedisPortalLinkIpRateLimiterTests
{
    private static readonly Guid Tenant = Guid.Parse("019ef3ba-2222-0000-0000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("019ef3ba-2222-0000-0000-000000000002");
    private const string Ip = "203.0.113.7";

    private static PortalLinkRateLimitOptions Limits(int max = 10, int window = 3600)
        => new() { MaxIssuesPerIp = max, IpWindowSeconds = window };

    /// <summary>A stateful fake IDatabase whose INCR round-trips through a dictionary; records EXPIRE calls.</summary>
    private static (RedisPortalLinkIpRateLimiter limiter, IDatabase db) BuildStateful(PortalLinkRateLimitOptions? opts = null)
    {
        var store = new Dictionary<string, long>();
        var db = Substitute.For<IDatabase>();

        db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = ci.Arg<RedisKey>().ToString()!;
                long next = (store.TryGetValue(key, out var v) ? v : 0) + ci.ArgAt<long>(1);
                store[key] = next;
                return next;
            });

        db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        var limiter = new RedisPortalLinkIpRateLimiter(
            mux, Options.Create(opts ?? Limits()), NullLogger<RedisPortalLinkIpRateLimiter>.Instance);
        return (limiter, db);
    }

    /// <summary>A fake whose INCR throws — used to prove FAIL-OPEN.</summary>
    private static RedisPortalLinkIpRateLimiter BuildThrowing()
    {
        var db = Substitute.For<IDatabase>();
        db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns<long>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "redis down"));

        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        return new RedisPortalLinkIpRateLimiter(
            mux, Options.Create(Limits()), NullLogger<RedisPortalLinkIpRateLimiter>.Instance);
    }

    [Fact]
    [Trait("TC", "TC-REC-011")]
    public async Task FirstTenAcquire_EleventhIsBlocked()
    {
        var (limiter, _) = BuildStateful(Limits(max: 10));

        for (int i = 0; i < 10; i++)
            (await limiter.TryAcquireAsync(Tenant, Ip)).Should().BeTrue($"issue #{i + 1} is within the cap");

        (await limiter.TryAcquireAsync(Tenant, Ip)).Should().BeFalse("the 11th issue is over the cap of 10");
    }

    [Fact]
    [Trait("TC", "TC-REC-011")]
    public async Task DifferentTenantIpKey_IsIndependent()
    {
        var (limiter, _) = BuildStateful(Limits(max: 10));

        // Exhaust the cap for (Tenant, Ip).
        for (int i = 0; i < 11; i++)
            await limiter.TryAcquireAsync(Tenant, Ip);

        // A different tenant with the SAME ip has its own counter and is unaffected.
        (await limiter.TryAcquireAsync(OtherTenant, Ip)).Should().BeTrue("a different tenant's key is independent");
        // A different IP under the same tenant is likewise independent.
        (await limiter.TryAcquireAsync(Tenant, "198.51.100.9")).Should().BeTrue("a different IP's key is independent");
    }

    [Fact]
    [Trait("TC", "TC-REC-011")]
    public async Task Expire_IsSetOnlyOnTheFirstHit()
    {
        var (limiter, db) = BuildStateful(Limits(window: 3600));

        await limiter.TryAcquireAsync(Tenant, Ip); // n == 1 → EXPIRE stamped
        await limiter.TryAcquireAsync(Tenant, Ip); // n == 2 → no EXPIRE

        // EXPIRE received exactly once, with the configured fixed-window TTL, on the first INCR only.
        await db.Received(1).KeyExpireAsync(
            Arg.Is<RedisKey>(k => k.ToString() == $"hrm:ratelimit:portal-link:{Tenant}:{Ip}"),
            Arg.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromSeconds(3600)),
            Arg.Any<ExpireWhen>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    [Trait("TC", "TC-REC-011")]
    public async Task RedisThrows_FailsOpen_Allows()
    {
        var limiter = BuildThrowing();

        // A Redis blip on the counter must degrade to ALLOW (never throw, never lock out a real applicant).
        (await limiter.TryAcquireAsync(Tenant, Ip)).Should().BeTrue("a Redis error must fail open (allow)");
    }
}
