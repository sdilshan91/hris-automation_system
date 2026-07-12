using FluentAssertions;
using HRM.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace HRM.Tests.Unit;

/// <summary>
/// P3-3 (NFR-2): Redis-backed <see cref="RedisPermissionCache"/>. Exercises the version-prefixed key scheme
/// (round-trip, per-user invalidate, and the KEY correctness test — a tenant-wide invalidate INCRs the version so a
/// value written under the OLD version is no longer returned WITHOUT any SCAN), plus the mandatory FAIL-OPEN
/// behaviour (a Redis blip must degrade to a cache miss / no-op, never throw and never deny) and the bounded TTL.
///
/// Redis is faked via NSubstitute over <see cref="IDatabase"/>/<see cref="IConnectionMultiplexer"/> (the thin-seam
/// pattern the P3-2 <c>SessionRevocationTests</c> use — no Docker/Testcontainer, which the agent rig lacks). A small
/// stateful in-memory backing store makes GET/SET/INCR/DEL round-trips authentic rather than pre-canned.
/// </summary>
public sealed class RedisPermissionCacheTests
{
    private static readonly Guid Tenant = Guid.Parse("019ef3ba-1111-0000-0000-000000000001");
    private static readonly Guid User = Guid.Parse("019ef3ba-1111-0000-0000-000000000002");

    /// <summary>A stateful fake IDatabase backed by a dictionary so GET/SET/INCR/DEL behave like a real key store.</summary>
    private static (RedisPermissionCache cache, IDatabase db) BuildStatefulCache()
    {
        var store = new Dictionary<string, RedisValue>();
        var db = Substitute.For<IDatabase>();

        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci => store.TryGetValue(ci.Arg<RedisKey>().ToString()!, out var v) ? v : RedisValue.Null);

        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                store[ci.Arg<RedisKey>().ToString()!] = ci.ArgAt<RedisValue>(1);
                return true;
            });

        db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = ci.Arg<RedisKey>().ToString()!;
                long current = store.TryGetValue(key, out var v) && v.TryParse(out long n) ? n : 0;
                long next = current + ci.ArgAt<long>(1);
                store[key] = next;
                return next;
            });

        db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci => store.Remove(ci.Arg<RedisKey>().ToString()!));

        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        var cache = new RedisPermissionCache(mux, NullLogger<RedisPermissionCache>.Instance);
        return (cache, db);
    }

    /// <summary>A fake whose every Redis call throws — used to prove FAIL-OPEN.</summary>
    private static RedisPermissionCache BuildThrowingCache()
    {
        var db = Substitute.For<IDatabase>();
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<RedisValue>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "redis down"));
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "redis down"));
        db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns<long>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "redis down"));
        db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "redis down"));

        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        return new RedisPermissionCache(mux, NullLogger<RedisPermissionCache>.Instance);
    }

    // ---- Round-trip + miss --------------------------------------------------------------------------------------

    [Fact]
    public async Task SetThenGet_RoundTripsPermissions()
    {
        var (cache, _) = BuildStatefulCache();
        var perms = new[] { "Leave.Apply", "Payroll.View" };

        await cache.SetPermissionsAsync(Tenant, User, perms);
        var result = await cache.GetPermissionsAsync(Tenant, User);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(perms);
    }

    [Fact]
    public async Task Get_ReturnsNull_OnMiss()
    {
        var (cache, _) = BuildStatefulCache();

        (await cache.GetPermissionsAsync(Tenant, User)).Should().BeNull();
    }

    // ---- Per-user invalidate ------------------------------------------------------------------------------------

    [Fact]
    public async Task InvalidateAsync_DeletesVersionedUserKey()
    {
        var (cache, _) = BuildStatefulCache();
        await cache.SetPermissionsAsync(Tenant, User, new[] { "Leave.Apply" });

        await cache.InvalidateAsync(Tenant, User);

        (await cache.GetPermissionsAsync(Tenant, User)).Should().BeNull();
    }

    // ---- KEY correctness test: tenant invalidate INCRs the version, orphaning old-version keys, no SCAN ---------

    [Fact]
    public async Task InvalidateTenantAsync_BumpsVersion_SoOldValueIsNoLongerReturned()
    {
        var (cache, db) = BuildStatefulCache();
        await cache.SetPermissionsAsync(Tenant, User, new[] { "Leave.Apply" });

        // Sanity: value is served under the current (v0) version.
        (await cache.GetPermissionsAsync(Tenant, User)).Should().NotBeNull();

        await cache.InvalidateTenantAsync(Tenant);

        // After the version INCR, Get resolves the NEW-version user key (absent) → miss. The old-version key was
        // never SCANned/deleted; it is simply orphaned and left to expire via its own TTL.
        (await cache.GetPermissionsAsync(Tenant, User)).Should().BeNull();

        // Proves the mechanism is an O(1) INCR on the version counter, not a keyspace SCAN.
        await db.Received(1).StringIncrementAsync(
            Arg.Is<RedisKey>(k => k.ToString() == $"hrm:perm:t:{Tenant}:ver"),
            Arg.Any<long>(),
            Arg.Any<CommandFlags>());

        // Re-caching under the new version becomes visible again.
        await cache.SetPermissionsAsync(Tenant, User, new[] { "Payroll.View" });
        (await cache.GetPermissionsAsync(Tenant, User)).Should().BeEquivalentTo(new[] { "Payroll.View" });
    }

    // ---- FAIL-OPEN ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_FailsOpen_ReturnsNull_WhenRedisThrows()
    {
        var cache = BuildThrowingCache();

        // A Redis blip on the read path must degrade to a cache miss (caller then resolves from DB), never throw.
        (await cache.GetPermissionsAsync(Tenant, User)).Should().BeNull();
    }

    [Fact]
    public async Task Set_FailsOpen_Swallows_WhenRedisThrows()
    {
        var cache = BuildThrowingCache();

        var act = async () => await cache.SetPermissionsAsync(Tenant, User, new[] { "Leave.Apply" });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Invalidate_FailsOpen_Swallows_WhenRedisThrows()
    {
        var cache = BuildThrowingCache();

        var act = async () => await cache.InvalidateAsync(Tenant, User);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidateTenant_FailsOpen_Swallows_WhenRedisThrows()
    {
        var cache = BuildThrowingCache();

        var act = async () => await cache.InvalidateTenantAsync(Tenant);
        await act.Should().NotThrowAsync();
    }

    // ---- Bounded TTL --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Set_UsesBounded15MinuteTtl()
    {
        var (cache, db) = BuildStatefulCache();

        await cache.SetPermissionsAsync(Tenant, User, new[] { "Leave.Apply" });

        // The per-user key carries a bounded ~15-min expiry — never a permanent key, so a missed invalidation
        // still self-heals within the TTL window.
        await db.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Is<TimeSpan?>(ttl => ttl.HasValue && ttl.Value > TimeSpan.FromMinutes(14) && ttl.Value <= TimeSpan.FromMinutes(15)),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }
}
