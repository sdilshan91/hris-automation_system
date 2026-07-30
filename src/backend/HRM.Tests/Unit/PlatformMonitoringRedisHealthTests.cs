// ============================================================================
// ISSUE-344: PlatformMonitoringService.GetPlatformHealthAsync must report the
// REAL Redis state, not an unconditional "NotConfigured".
//   - Redis configured + reachable  ⇒ Healthy   (old always-NotConfigured FAILS)
//   - Redis configured + unreachable ⇒ Down      (old always-NotConfigured FAILS)
//   - Redis genuinely not configured ⇒ NotConfigured, and the multiplexer is
//     never pinged (a naive "always ping" fix FAILS this arm).
// EF Core InMemory through the real service (no Testcontainers).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Monitoring.DTOs;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;

namespace HRM.Tests.Unit;

public sealed class PlatformMonitoringRedisHealthTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public PlatformMonitoringRedisHealthTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    private AppDbContext Db()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(true);
        return TestDbContextFactory.Create(ctx, _dbName);
    }

    private static IConfiguration Config(string? redisConn) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(redisConn is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = redisConn })
            .Build();

    private PlatformMonitoringService Service(IConfiguration config, IConnectionMultiplexer? redis)
    {
        var jobQueue = Substitute.For<IJobQueueMonitor>();
        jobQueue.GetSnapshot().Returns(new JobQueueSnapshotDto(
            Available: true, Enqueued: 0, Processing: 0, Scheduled: 0, Succeeded: 0, Failed: 0));
        return new(Db(), _currentUser, jobQueue, config,
            Substitute.For<ILogger<PlatformMonitoringService>>(), redis);
    }

    private static (IConnectionMultiplexer mux, IDatabase db) MuxThatPings(Func<Task<TimeSpan>> ping)
    {
        var db = Substitute.For<IDatabase>();
        db.PingAsync(Arg.Any<CommandFlags>()).Returns(_ => ping());
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        return (mux, db);
    }

    [Fact]
    public async Task Configured_AndReachable_ReportsHealthy()
    {
        // Old always-NotConfigured impl returns NotConfigured here ⇒ this assertion FAILS it.
        var (mux, _) = MuxThatPings(() => Task.FromResult(TimeSpan.FromMilliseconds(1)));

        var result = await Service(Config("localhost:6379"), mux).GetPlatformHealthAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.RedisHealth.Should().Be(DependencyHealthStatus.Healthy);
    }

    [Fact]
    public async Task Configured_ButUnreachable_ReportsDown()
    {
        // Old always-NotConfigured impl returns NotConfigured here ⇒ this assertion FAILS it.
        var (mux, _) = MuxThatPings(() => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var result = await Service(Config("localhost:6379"), mux).GetPlatformHealthAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.RedisHealth.Should().Be(DependencyHealthStatus.Down);
    }

    [Fact]
    public async Task NotConfigured_ReportsNotConfigured_AndDoesNotPing()
    {
        // Blank config + a live multiplexer: still NotConfigured (Redis is optional). A naive "multiplexer present
        // ⇒ ping" fix would report Healthy AND call PingAsync ⇒ both assertions FAIL that fix.
        var (mux, db) = MuxThatPings(() => Task.FromResult(TimeSpan.Zero));

        var result = await Service(Config(redisConn: null), mux).GetPlatformHealthAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.RedisHealth.Should().Be(DependencyHealthStatus.NotConfigured);
        await db.DidNotReceive().PingAsync(Arg.Any<CommandFlags>());
    }
}
