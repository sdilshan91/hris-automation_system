// ============================================================================
// TC-ADM-002-15/-16 — the GlitchTip metrics read path.
//
// The contract that matters here is FAIL-SOFT. GlitchTip is an optional, separately-deployed
// component; a monitoring read must never break the dashboard it feeds. Unconfigured, down,
// non-2xx, or a garbage payload must all yield EMPTY — and empty must be rendered as "not
// available", never as "zero errors", which reads as healthy.
//
// Payload shapes below are the REAL ones captured from the live instance on 2026-08-04, not
// invented: stats_v2 returns {intervals, groups[].series}, and /issues/ returns an array with
// title/count/level/lastSeen.
// ============================================================================

using System.Net;
using FluentAssertions;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class GlitchTipMetricsClientTests
{
    private sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("connection refused");
    }

    private static GlitchTipMetricsClient Client(HttpMessageHandler handler, bool configured = true)
    {
        var settings = configured
            ? new Dictionary<string, string?>
            {
                ["GlitchTip:ApiBaseUrl"] = "http://localhost:8000",
                ["GlitchTip:ApiToken"] = "test-token",
                ["GlitchTip:Organization"] = "hrm",
            }
            : [];

        return new GlitchTipMetricsClient(
            new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<GlitchTipMetricsClient>.Instance);
    }

    // ── configuration gate ───────────────────────────────────────────────

    [Fact]
    public void IsConfigured_IsFalse_WithoutTokenOrOrg()
    {
        Client(new StubHandler(HttpStatusCode.OK, "[]"), configured: false).IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Unconfigured_ReturnsEmpty_WithoutCallingOut()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var c = Client(handler, configured: false);

        (await c.GetTopErrorsAsync(null)).Should().BeEmpty();
        handler.LastUrl.Should().BeNull("an unconfigured client must not issue a request at all");
    }

    // ── fail-soft ────────────────────────────────────────────────────────

    [Fact]
    public async Task InstanceDown_ReturnsEmpty_RatherThanThrowing()
    {
        var c = Client(new ThrowingHandler());

        var act = async () => await c.GetTopErrorsAsync(null);
        await act.Should().NotThrowAsync("GlitchTip being down must never break the monitoring dashboard");
        (await c.GetTopErrorsAsync(null)).Should().BeEmpty();
    }

    /// <summary>
    /// The body here is deliberately VALID, populated JSON. An earlier version used garbage, which meant this
    /// arm passed even with the status check deleted — the payload simply failed to parse, so it returned empty
    /// for the wrong reason. With a parseable body, dropping the status check would surface an error-page body
    /// AS MONITORING DATA, and this arm catches it. (Found by a surviving mutant, 2026-08-04.)
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task NonSuccessStatus_ReturnsEmpty_EvenWhenTheBodyIsParseable(HttpStatusCode code)
    {
        const string parseableBody = "[{\"title\":\"must-not-be-surfaced\",\"count\":99,\"level\":\"error\"}]";

        var result = await Client(new StubHandler(code, parseableBody)).GetTopErrorsAsync(null);

        result.Should().BeEmpty($"a {(int)code} body must never be rendered as monitoring data");
    }

    [Fact]
    public async Task GarbagePayload_ReturnsEmpty_RatherThanThrowing()
    {
        var c = Client(new StubHandler(HttpStatusCode.OK, "{ not json at all"));
        (await c.GetTopErrorsAsync(null)).Should().BeEmpty();
    }

    // ── real payload shapes ──────────────────────────────────────────────

    [Fact]
    public async Task TopErrors_ParsesTheLivePayload_AndSortsByCount()
    {
        const string body = """
        [
          {"title":"NullReferenceException","count":"3","level":"error","lastSeen":"2026-08-03T21:00:00Z"},
          {"title":"TimeoutException","count":12,"level":"fatal","lastSeen":"2026-08-03T22:00:00Z"}
        ]
        """;
        var result = await Client(new StubHandler(HttpStatusCode.OK, body)).GetTopErrorsAsync(null);

        result.Should().HaveCount(2);
        result[0].Title.Should().Be("TimeoutException", "most frequent first");
        result[0].Count.Should().Be(12);
        result[0].Level.Should().Be("fatal");
        // `count` arrives as a STRING in the live payload for some issues and a number for others.
        result[1].Count.Should().Be(3, "a string-encoded count must parse, not silently become 0");
    }

    [Fact]
    public async Task TopErrors_ForATenant_SendsTheTenantIdTagFilter()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var tenantId = Guid.NewGuid();

        await Client(handler).GetTopErrorsAsync(tenantId);

        handler.LastUrl.Should().Contain(Uri.EscapeDataString($"tenant_id:{tenantId}"),
            "the tenant filter is verified to DISCRIMINATE server-side, so it must actually be sent");
    }

    [Fact]
    public async Task ErrorTrend_ParsesIntervalsAndSeries_Positionally()
    {
        const string body = """
        {
          "intervals":["2026-08-03T20:00:00+00:00","2026-08-03T21:00:00+00:00"],
          "groups":[{"series":{"sum(quantity)":[4,7]}}]
        }
        """;
        var now = new DateTime(2026, 8, 3, 22, 0, 0, DateTimeKind.Utc);
        var points = await Client(new StubHandler(HttpStatusCode.OK, body))
            .GetErrorTrendAsync(null, now.AddHours(-24), now);

        points.Should().HaveCount(2);
        points[0].Count.Should().Be(4);
        points[1].Count.Should().Be(7, "series values align positionally with intervals");
    }

    [Fact]
    public async Task ErrorTrend_WithNoSeries_ReportsZeroCounts_NotAnException()
    {
        // The live instance returns intervals with NO groups when nothing has errored — the exact shape
        // observed on 2026-08-04. It must degrade to zeroed points, not blow up.
        const string body = """{"intervals":["2026-08-03T20:00:00+00:00"],"groups":[]}""";
        var now = new DateTime(2026, 8, 3, 22, 0, 0, DateTimeKind.Utc);

        var points = await Client(new StubHandler(HttpStatusCode.OK, body))
            .GetErrorTrendAsync(null, now.AddHours(-24), now);

        points.Should().ContainSingle();
        points[0].Count.Should().Be(0);
    }

    /// <summary>
    /// stats_v2 aggregates org-wide and does not accept the issue-search grammar, so it cannot answer a
    /// per-tenant trend. Returning platform-wide numbers under a tenant heading would be a silent lie, so the
    /// client returns empty instead. Pinned so nobody "fixes" it by dropping the tenant argument.
    /// </summary>
    [Fact]
    public async Task ErrorTrend_ForASpecificTenant_ReturnsEmpty_RatherThanPlatformWideNumbers()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"intervals":["2026-08-03T20:00:00+00:00"],"groups":[{"series":{"sum(quantity)":[99]}}]}""");

        var points = await Client(handler).GetErrorTrendAsync(Guid.NewGuid(), DateTime.UtcNow.AddHours(-24), DateTime.UtcNow);

        points.Should().BeEmpty();
        handler.LastUrl.Should().BeNull("it must not even ask, rather than ask and mislabel the answer");
    }
}
