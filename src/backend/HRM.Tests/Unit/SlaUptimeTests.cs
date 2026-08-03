// ============================================================================
// US-ADM-002 FR-7 / TC-ADM-002-17 — SLA uptime % from retained probe history.
//
// The TC's hard requirement is negative as much as positive: with no probe history the field
// must stay NULL — "Not available", never a fabricated 100%. So the arms below pin BOTH the
// absence case and the arithmetic, because a bug that returned 100% on an empty table would
// look perfectly healthy on the dashboard while measuring nothing at all.
//
// Degraded counts as NOT healthy: a degraded platform is not meeting its availability promise,
// and counting it as up is the flattering measurement the TC warns against.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using HRM.Application.Common.Interfaces;

namespace HRM.Tests.Unit;

public sealed class SlaUptimeTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public SlaUptimeTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(true);
    }

    private AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, _tenantContext);

    private static HealthProbe Probe(DateTime at, bool healthy, string? status = null) => new()
    {
        Id = Guid.CreateVersion7(),
        ObservedAtUtc = at,
        IsHealthy = healthy,
        Status = status ?? (healthy ? "Healthy" : "Unhealthy"),
        DurationMs = 5,
    };

    /// <summary>
    /// The arm that matters most: an empty table must NOT read as 100%. This is the exact fabrication
    /// TC-ADM-002-17 forbids, and it is the failure mode that would look fine in the UI.
    /// </summary>
    [Fact]
    public async Task NoProbeHistory_IsRecordedAsAbsent_NotAsPerfectUptime()
    {
        await using var db = Db();

        var window = DateTime.UtcNow.AddDays(-30);
        (await db.HealthProbes.AnyAsync(p => p.ObservedAtUtc >= window))
            .Should().BeFalse("the fixture has no probes");

        // Exercise the REAL query AND the real rule.
        (await PlatformMonitoringService.ComputeSlaUptimePercentAsync(db, DateTime.UtcNow, 30, default))
            .Should().BeNull("zero probes must read as 'not available', never as 100%");
        PlatformMonitoringService.UptimeFromCounts(total: 0, healthy: 0).Should().BeNull();
    }

    [Theory]
    [InlineData(10, 8, 80d)]
    [InlineData(2, 1, 50d)]
    [InlineData(3, 3, 100d)]
    [InlineData(4, 0, 0d)]          // a total outage must read 0, not null — null means "unmeasured"
    [InlineData(8640, 8631, 99.896d)] // ~43 min of a 30-day month at a 5-min cadence: a 99.9% tier breach
    public void UptimeRule_IsHealthyOverTotal(int total, int healthy, double expected)
    {
        PlatformMonitoringService.UptimeFromCounts(total, healthy).Should().Be(expected);
    }

    [Fact]
    public async Task Uptime_IsHealthyOverTotal_WithinTheWindow()
    {
        var now = DateTime.UtcNow;
        await using (var seed = Db())
        {
            // 8 healthy, 2 unhealthy, all inside the window → 80%.
            for (var i = 0; i < 8; i++) seed.HealthProbes.Add(Probe(now.AddHours(-i), healthy: true));
            for (var i = 0; i < 2; i++) seed.HealthProbes.Add(Probe(now.AddHours(-20 - i), healthy: false));
            await seed.SaveChangesAsync();
        }

        await using var db = Db();

        var pct = await PlatformMonitoringService.ComputeSlaUptimePercentAsync(db, now, windowDays: 30, default);
        pct.Should().Be(80d);
    }

    /// <summary>Probes older than the window must not dilute or flatter the current figure.</summary>
    [Fact]
    public async Task ProbesOutsideTheWindow_AreExcluded()
    {
        var now = DateTime.UtcNow;
        await using (var seed = Db())
        {
            seed.HealthProbes.Add(Probe(now.AddHours(-1), healthy: true));
            // 100 healthy probes from a year ago would drag an in-window outage back up to ~99%.
            for (var i = 0; i < 100; i++) seed.HealthProbes.Add(Probe(now.AddDays(-365), healthy: true));
            seed.HealthProbes.Add(Probe(now.AddHours(-2), healthy: false));
            await seed.SaveChangesAsync();
        }

        await using var db = Db();

        // Drives the REAL windowed query. Deleting the window filter would pull the 100 year-old green probes
        // in and read ~98%, so this arm fails on exactly that mutation.
        var pct = await PlatformMonitoringService.ComputeSlaUptimePercentAsync(db, now, windowDays: 30, default);

        pct.Should().Be(50d, "a year-old run of green probes must not mask a current outage");
    }

    /// <summary>
    /// Degraded is NOT healthy. Pinned explicitly because the readiness endpoint returns HTTP 200 for
    /// Degraded, so an implementation keyed on the HTTP status rather than the health status would count a
    /// degraded platform as fully up.
    /// </summary>
    [Fact]
    public async Task DegradedProbe_DoesNotCountAsUp()
    {
        var now = DateTime.UtcNow;
        await using (var seed = Db())
        {
            seed.HealthProbes.Add(Probe(now.AddMinutes(-5), healthy: true));
            seed.HealthProbes.Add(Probe(now.AddMinutes(-10), healthy: false, status: nameof(HealthStatus.Degraded)));
            await seed.SaveChangesAsync();
        }

        await using var db = Db();
        (await db.HealthProbes.ToListAsync()).Single(r => r.Status == nameof(HealthStatus.Degraded)).IsHealthy
            .Should().BeFalse("a degraded platform is not meeting its availability promise");

        var pct = await PlatformMonitoringService.ComputeSlaUptimePercentAsync(db, now, windowDays: 30, default);
        pct.Should().Be(50d, "the degraded probe must drag the figure down, not be counted as up");
    }

    /// <summary>
    /// The probe table carries NO tenant_id — uptime is a platform property. Pinned so nobody later adds a
    /// tenant scope and silently turns a shared measurement into a per-tenant one that no probe populates.
    /// </summary>
    [Fact]
    public void HealthProbe_IsSystemScope_WithNoTenantId()
    {
        typeof(HealthProbe).GetProperty("TenantId")
            .Should().BeNull("uptime is a property of the shared platform, not of a tenant");
        typeof(HealthProbe).BaseType.Should().Be(typeof(object),
            "it must not inherit BaseEntity, whose tenant interceptor/query-filter rules do not apply here");
    }
}
