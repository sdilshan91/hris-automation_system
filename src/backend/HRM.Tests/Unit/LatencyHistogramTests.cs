// ============================================================================
// TC-ADM-002-14 (P95) / -16 (latency trend) — the per-request latency meter.
//
// Two contracts, and the second matters more than the first:
//
//   1. The P95 rule: null on an empty window (never 0 — zero renders as a perfectly fast
//      service), interpolated within the containing bucket, overflow reported as a floor.
//
//   2. FAIL-OPEN. This meter runs on EVERY API request. A bug in it must never fail a
//      request — that is a far worse outcome than a missing dashboard number. The counter
//      is exercised under concurrency here because a lock-free buffer that loses counts
//      under load would silently understate the denominator of the error rate.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class LatencyHistogramTests
{
    // ── bucket mapping ───────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]        // instant → first bucket
    [InlineData(5, 0)]        // ON the boundary → that bucket (bounds are inclusive upper)
    [InlineData(5.1, 1)]      // just past it → next
    [InlineData(100, 4)]
    [InlineData(10000, 10)]   // the last finite bound
    [InlineData(10001, 11)]   // overflow
    [InlineData(999999, 11)]  // an outlier is COUNTED, never dropped
    public void IndexFor_MapsElapsedToTheRightBucket(double ms, int expected)
    {
        LatencyBuckets.IndexFor(ms).Should().Be(expected);
    }

    [Fact]
    public void BucketCount_IncludesTheOverflowCell()
    {
        LatencyBuckets.Count.Should().Be(LatencyBuckets.UpperBoundsMs.Length + 1);
    }

    // ── the P95 rule ─────────────────────────────────────────────────────

    /// <summary>
    /// The arm that matters most: no requests must read as "not measured", not as instant. A 0 here would
    /// render as a perfectly fast service on the dashboard — the same fabrication the SLA uptime field refuses.
    /// </summary>
    [Fact]
    public void P95_OnAnEmptyWindow_IsNull_NotZero()
    {
        TenantLatencyUsage.P95From(new Dictionary<int, long>()).Should().BeNull();
        TenantLatencyUsage.P95From(new Dictionary<int, long> { [0] = 0 }).Should().BeNull();
    }

    [Fact]
    public void P95_WithEverythingInOneBucket_LandsInsideThatBucket()
    {
        // 100 requests all in bucket 2 (10 < ms <= 25) → P95 must be within (10, 25].
        var p95 = TenantLatencyUsage.P95From(new Dictionary<int, long> { [2] = 100 });

        p95.Should().NotBeNull();
        p95!.Value.Should().BeInRange(10d, 25d);
    }

    /// <summary>
    /// The boundary, pinned deliberately because it is counter-intuitive and I got it wrong first: with EXACTLY
    /// 95 of 100 requests fast, the 95th percentile IS the fast bucket's bound — 95% of observations fall at or
    /// below it. That is the textbook definition and what Prometheus's histogram_quantile returns. A tail only
    /// moves P95 once it exceeds 5% of the population.
    /// </summary>
    [Fact]
    public void P95_WithExactlyFivePercentSlow_SitsAtTheFastBoundary()
    {
        var p95 = TenantLatencyUsage.P95From(new Dictionary<int, long> { [0] = 95, [8] = 5 });

        p95.Should().Be(LatencyBuckets.UpperBoundsMs[0],
            "95 of 100 observations are at or below bucket 0's bound, so that IS the 95th percentile");
    }

    /// <summary>
    /// The discriminating case: once the slow tail exceeds 5%, P95 must land IN it. An implementation that
    /// ignored the tail would keep reporting a fast bucket while more than 5% of users waited seconds.
    /// </summary>
    [Fact]
    public void P95_IsDraggedUpOnceTheSlowTailExceedsFivePercent()
    {
        var fastOnly = TenantLatencyUsage.P95From(new Dictionary<int, long> { [0] = 100 });
        var withTail = TenantLatencyUsage.P95From(new Dictionary<int, long> { [0] = 90, [8] = 10 });

        fastOnly.Should().BeLessThanOrEqualTo(5d);
        withTail.Should().BeGreaterThan(1000d, "a 10% slow tail is exactly what a P95 exists to expose");
    }

    [Fact]
    public void P95_InTheOverflowBucket_ReportsTheLastFiniteBoundAsAFloor()
    {
        // Everything beyond 10 s: there is no upper bound to interpolate against, so report the floor rather
        // than inventing a ceiling.
        var p95 = TenantLatencyUsage.P95From(new Dictionary<int, long> { [LatencyBuckets.UpperBoundsMs.Length] = 50 });

        p95.Should().Be(LatencyBuckets.UpperBoundsMs[^1]);
    }

    // ── the buffer: lock-free, lossless, hourly ──────────────────────────

    [Fact]
    public void RecordLatency_BucketsByHour_SoTheTrendIsHourly()
    {
        var counter = new ApiCallCounter();
        var tenant = Guid.NewGuid();
        var h1 = new DateTime(2026, 8, 3, 10, 15, 0, DateTimeKind.Utc);
        var h2 = new DateTime(2026, 8, 3, 11, 45, 0, DateTimeKind.Utc);

        counter.RecordLatency(tenant, h1, 7);
        counter.RecordLatency(tenant, h2, 7);

        var deltas = counter.DrainLatency();
        deltas.Should().HaveCount(2, "different hours are different cells");
        deltas.Select(d => d.HourUtc).Should().OnlyContain(h => h.Minute == 0 && h.Second == 0,
            "the key must be truncated to the hour");
    }

    [Fact]
    public void DrainLatency_IsReadAndZero_SoASecondDrainIsEmpty()
    {
        var counter = new ApiCallCounter();
        counter.RecordLatency(Guid.NewGuid(), DateTime.UtcNow, 12);

        counter.DrainLatency().Should().ContainSingle();
        counter.DrainLatency().Should().BeEmpty("a drained cell must not be flushed twice");
    }

    /// <summary>
    /// The buffer feeds the error-rate DENOMINATOR, so lost increments would silently inflate the error rate.
    /// Drives real concurrency rather than asserting the happy path on one thread.
    /// </summary>
    [Fact]
    public async Task RecordLatency_LosesNothingUnderConcurrency()
    {
        var counter = new ApiCallCounter();
        var tenant = Guid.NewGuid();
        var hour = new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);
        const int perTask = 500, tasks = 8;

        await Task.WhenAll(Enumerable.Range(0, tasks).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perTask; i++) counter.RecordLatency(tenant, hour, 7);
        })));

        counter.DrainLatency().Sum(d => d.Count).Should().Be(perTask * tasks);
    }

    // ── the trend is LATENCY, not volume ─────────────────────────────────

    /// <summary>
    /// TC-ADM-002-16 pinned as a rule, because I wired this wrong first: a field named "latency trend" must
    /// carry LATENCY. Plotting request COUNTS there would rise at busy times and fall overnight while saying
    /// nothing about how slow the service was — a mislabelled number, the same class of defect as putting an
    /// error count in a field named "...Percent".
    ///
    /// Two hours with IDENTICAL volume but very different latency must produce different trend points; an
    /// implementation that emitted volume would report them as equal.
    /// </summary>
    [Fact]
    public void HourlyTrend_DistinguishesLatency_NotJustVolume()
    {
        // Same 100 requests each hour; hour A fast, hour B slow.
        var fastHour = TenantLatencyUsage.P95From(new Dictionary<int, long> { [0] = 100 });
        var slowHour = TenantLatencyUsage.P95From(new Dictionary<int, long> { [8] = 100 });

        fastHour.Should().NotBe(slowHour,
            "equal volume with different latency must NOT produce the same trend point");
        slowHour.Should().BeGreaterThan(fastHour!.Value);
    }

    /// <summary>An hour with no traffic is a GAP, not a zero — "no requests" differs from "instant responses".</summary>
    [Fact]
    public void HourWithNoTraffic_HasNoP95_RatherThanZero()
    {
        TenantLatencyUsage.P95From(new Dictionary<int, long>()).Should().BeNull();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-5)]
    public void RecordLatency_WithAClockAnomaly_IsClamped_NotCorrupting(double elapsed)
    {
        var counter = new ApiCallCounter();
        var act = () => counter.RecordLatency(Guid.NewGuid(), DateTime.UtcNow, elapsed);

        act.Should().NotThrow("a clock anomaly must never propagate out of a meter on the request path");
        counter.DrainLatency().Should().ContainSingle().Which.BucketIndex.Should().Be(0);
    }
}
