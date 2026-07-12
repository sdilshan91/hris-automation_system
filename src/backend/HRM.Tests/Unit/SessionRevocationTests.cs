using System.Security.Claims;
using FluentAssertions;
using HRM.Api.Auth;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace HRM.Tests.Unit;

/// <summary>
/// P3-2: real JWT access-token denylist / session revocation. Covers the Redis-backed cutoff store
/// (<see cref="RedisTokenDenylist"/>), the <see cref="ISessionRevoker"/> wiring, the no-op fallbacks used when
/// Redis is absent, and the <c>OnTokenValidated</c> decision (<see cref="SessionRevocationCheck"/>) — including the
/// mandatory FAIL-OPEN behaviour so a Redis blip can never lock every user out.
///
/// The Redis calls are faked via NSubstitute over <see cref="IDatabase"/>/<see cref="IConnectionMultiplexer"/>
/// (no Docker/Testcontainer required — the agent rig has no Redis), which is the "thin fakeable seam over IDatabase"
/// the P3-2 contract permits when a Redis Testcontainer isn't available.
/// </summary>
public sealed class SessionRevocationTests
{
    private static readonly Guid User = Guid.Parse("019ef3ba-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = Guid.Parse("019ef3ba-0000-0000-0000-000000000002");

    private static (RedisTokenDenylist denylist, IDatabase db) BuildRedisDenylist()
    {
        var db = Substitute.For<IDatabase>();
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        var denylist = new RedisTokenDenylist(mux, NullLogger<RedisTokenDenylist>.Instance);
        return (denylist, db);
    }

    // ---- RedisTokenDenylist: RevokeAsync writes the cutoff with a bounded TTL ---------------------------------

    [Fact]
    public async Task RevokeAsync_writes_cutoff_key_with_bounded_ttl()
    {
        var (denylist, db) = BuildRedisDenylist();
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        await denylist.RevokeAsync(User, Tenant);

        // The cutoff key is per-(tenant,user) and the TTL is bounded to ~16 min (access-token life + buffer) so it
        // self-expires — never a permanent key, never unbounded growth.
        await db.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString()!.Contains(Tenant.ToString()) && k.ToString()!.Contains(User.ToString())),
            Arg.Any<RedisValue>(),
            Arg.Is<TimeSpan?>(ttl => ttl.HasValue && ttl.Value > TimeSpan.FromMinutes(15) && ttl.Value <= TimeSpan.FromMinutes(16)),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RevokeAsync_never_throws_when_redis_errors()
    {
        var (denylist, db) = BuildRedisDenylist();
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw new InvalidOperationException("redis down"));

        // Must not propagate into the offboarding/logout flow.
        var act = async () => await denylist.RevokeAsync(User, Tenant);
        await act.Should().NotThrowAsync();
    }

    // ---- RedisTokenDenylist: IsRevokedAsync cutoff comparison -------------------------------------------------

    [Fact]
    public async Task IsRevokedAsync_true_for_iat_before_cutoff_and_false_for_iat_after()
    {
        var (denylist, db) = BuildRedisDenylist();
        var cutoffUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns((RedisValue)cutoffUnix);

        (await denylist.IsRevokedAsync(User, Tenant, cutoffUnix - 5)).Should().BeTrue();  // issued before revoke
        (await denylist.IsRevokedAsync(User, Tenant, cutoffUnix + 5)).Should().BeFalse(); // issued after revoke
    }

    [Fact]
    public async Task IsRevokedAsync_false_when_no_cutoff_key()
    {
        var (denylist, db) = BuildRedisDenylist();
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        (await denylist.IsRevokedAsync(User, Tenant, DateTimeOffset.UtcNow.ToUnixTimeSeconds())).Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_fails_open_when_redis_errors()
    {
        var (denylist, db) = BuildRedisDenylist();
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<RedisValue>(_ => throw new InvalidOperationException("redis down"));

        // FAIL OPEN: a Redis error must be treated as "not revoked", never as a lockout.
        (await denylist.IsRevokedAsync(User, Tenant, DateTimeOffset.UtcNow.ToUnixTimeSeconds())).Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_fails_open_when_cutoff_value_unparseable()
    {
        var (denylist, db) = BuildRedisDenylist();
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns((RedisValue)"not-a-number");

        (await denylist.IsRevokedAsync(User, Tenant, DateTimeOffset.UtcNow.ToUnixTimeSeconds())).Should().BeFalse();
    }

    // ---- RedisSessionRevoker: wires ISessionRevoker.RevokeActiveSessionsAsync onto the denylist ---------------

    [Fact]
    public async Task RedisSessionRevoker_delegates_to_denylist_RevokeAsync()
    {
        var denylist = Substitute.For<ITokenDenylist>();
        var revoker = new RedisSessionRevoker(denylist, NullLogger<RedisSessionRevoker>.Instance);

        await revoker.RevokeActiveSessionsAsync(User, Tenant);

        await denylist.Received(1).RevokeAsync(User, Tenant, Arg.Any<CancellationToken>());
    }

    // ---- No-op fallbacks (Redis absent) are unchanged --------------------------------------------------------

    [Fact]
    public async Task NoOpTokenDenylist_never_reports_revoked()
    {
        var denylist = new NoOpTokenDenylist();

        await denylist.RevokeAsync(User, Tenant); // no-op
        (await denylist.IsRevokedAsync(User, Tenant, 0)).Should().BeFalse();
    }

    [Fact]
    public async Task NoOpSessionRevoker_completes_without_error()
    {
        var revoker = new NoOpSessionRevoker(NullLogger<NoOpSessionRevoker>.Instance);

        var act = async () => await revoker.RevokeActiveSessionsAsync(User, Tenant);
        await act.Should().NotThrowAsync();
    }

    // ---- OnTokenValidated decision (SessionRevocationCheck) ---------------------------------------------------

    /// <summary>An in-memory denylist with a fixed cutoff, so we exercise the real claim-parse + delegation path.</summary>
    private sealed class CutoffDenylist(long cutoffUnix) : ITokenDenylist
    {
        public Task RevokeAsync(Guid userId, Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsRevokedAsync(Guid userId, Guid tenantId, long tokenIssuedAtUnix, CancellationToken ct = default)
            => Task.FromResult(tokenIssuedAtUnix < cutoffUnix);
    }

    private static ClaimsPrincipal Principal(Guid userId, Guid tenantId, long iatUnix) =>
        new(new ClaimsIdentity(
        [
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("iat", iatUnix.ToString()),
        ], "TestAuth"));

    [Fact]
    public async Task Check_rejects_token_issued_before_revoke_and_accepts_one_issued_after()
    {
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var denylist = new CutoffDenylist(cutoff);

        // Token issued before the revoke cutoff → revoked (OnTokenValidated would context.Fail → 401).
        (await SessionRevocationCheck.IsRevokedAsync(Principal(User, Tenant, cutoff - 30), denylist))
            .Should().BeTrue();

        // Token issued after the revoke cutoff → still valid.
        (await SessionRevocationCheck.IsRevokedAsync(Principal(User, Tenant, cutoff + 30), denylist))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Check_fails_open_when_denylist_absent()
    {
        // Redis absent → ITokenDenylist not resolved (null). Token must be accepted (fail-open).
        (await SessionRevocationCheck.IsRevokedAsync(Principal(User, Tenant, 100), denylist: null))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Check_fails_open_when_denylist_throws()
    {
        var denylist = Substitute.For<ITokenDenylist>();
        denylist.IsRevokedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns<bool>(_ => throw new InvalidOperationException("redis down"));

        (await SessionRevocationCheck.IsRevokedAsync(Principal(User, Tenant, 100), denylist))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Check_fails_open_when_identity_claims_missing()
    {
        var denylist = new CutoffDenylist(long.MaxValue); // would revoke everything if reached
        var principal = new ClaimsPrincipal(new ClaimsIdentity("TestAuth")); // no sub/tenant_id/iat

        (await SessionRevocationCheck.IsRevokedAsync(principal, denylist)).Should().BeFalse();
    }
}
